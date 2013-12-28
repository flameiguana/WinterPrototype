using UnityEngine;
using System.Collections;
using System;

//TODO:
//1. Sync positions to server and then to clients (this relieves most players except host)
//2. Confirm hit on server (majority vote?, trust 1 client?)
public class Player : MonoBehaviour {


	public Rigidbody2D pellet;
	NetworkPlayer theOwner;
	bool isOwner = false;
	/* Movement variables */
	bool onGround = false;
	bool jump = false;
	bool facingRight = true;
	bool canShoot = true;

	float width;
	const float groundRadius = 0.2f;
	public Transform groundCheck;
	public Transform mouth;
	public LayerMask ground;
	public float JUMP_FORCE;
	public float SHOOT_FORCE;

	/* Synchronization Variables */
	private float currentSmooth;
	private float syncDelay;
	private float lastSynctime;
	private bool canStart= false;
	float lerpDelay;

	//
	float simTime;

	struct State{
		public Vector3 position;
		public bool facingRight;
		public float remoteTime;
		public float localTime;
		public State(Vector3 position, bool facingRight){
			this.position = position;
			this.facingRight = facingRight;
			this.remoteTime = float.NaN;
			this.localTime = float.NaN;
		}
	}

	CircularBuffer<State> states;

	[RPC]
	void SetPlayerID(NetworkPlayer player)
	{
		theOwner = player;
		if(player == Network.player){
			isOwner = true;
			rigidbody2D.isKinematic = false;
		}
	}

	//To server
	[RPC]
	void ShootRequest(int facingRight, Vector3 mouthPosition)
	{
		//shoot from where the player currently is (this might still be buggy because position on server is interpolated)
		networkView.RPC("ShotFired", RPCMode.All, facingRight, mouthPosition);
	}

	//To players (and host client)
	[RPC]
	void ShotFired(int facingRight, Vector3 mouthPosition){

		Rigidbody2D projectile = (Rigidbody2D)Instantiate (pellet, mouthPosition, Quaternion.identity);
		Projectile info = projectile.GetComponent<Projectile>();
		info.theOwner = theOwner;
		//try doing a constant velocity for better look/to make this relevant to grappling hook
		if(facingRight == 1)
			projectile.AddForce(new Vector2(SHOOT_FORCE, 0f));
		else
			projectile.AddForce(new Vector2(-SHOOT_FORCE, 0f));
	}

	void Turn(){
		facingRight = !facingRight;
		Vector3 localScale = transform.localScale;
		localScale.x *= -1;
		transform.localScale = localScale;
	}

	void Awake()
	{
		rigidbody2D.isKinematic = true;
		states = new CircularBuffer<State>(3);
		lastSynctime = float.PositiveInfinity;
		syncDelay = 0f;
	}
	// Use this for initialization
	void Start ()
	{
		BoxCollider2D collider = gameObject.GetComponent<BoxCollider2D>();
		width = collider.size.x;
		//localPosition = transform.position;
		lerpDelay = 2f / Network.sendRate; //with tick rate 25, and delay of 2 frames, we have .08s delay

	}


	void FixedUpdate()
	{
		if (isOwner){
			onGround = Physics2D.OverlapCircle(groundCheck.position , groundRadius);
			float up = jump ? 1.0f : 0.0f;
			Vector3 axes = new Vector3(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"), up);

			float speed = 6.0f; //6 units per second
			rigidbody2D.velocity = new Vector2(axes.x * speed, rigidbody2D.velocity.y);

			if(facingRight && axes.x < 0 || !facingRight && axes.x > 0)
				Turn ();

			if(jump){
				rigidbody2D.AddForce(new Vector2(0, JUMP_FORCE));
				jump = false;
			}
		}
		else{

		}
	}

	// Update is called once per frame
	void Update ()
	{
		if(isOwner){
			if(onGround && Input.GetKeyDown(KeyCode.Space)){
				jump = true;
			}
			if(canShoot && Input.GetKeyDown(KeyCode.Q)){
				networkView.RPC("ShootRequest", RPCMode.Server, Convert.ToInt32(facingRight), mouth.position);
			}
		}
		else{
			if(canStart){
				//Debug.Log("now interpolating");

				//read the two oldest states.
				State oldState = states.ReadOldest();
				
				State newState = states.ReadAt(1);

				float interval = newState.localTime - oldState.localTime;
				currentSmooth += Time.deltaTime;


				simTime = Time.time - lerpDelay; // this might be risky if we skip a frame



				if(newState.facingRight != facingRight)
					Turn();
				//Vector3 positionDifference = serverPosition - localPosition;
				Vector3 positionDifference = newState.position - oldState.position;
				float distanceApart = positionDifference.magnitude;
				if(distanceApart > width * 5.0f){
					transform.position = newState.position;
					oldState.position = newState.position;
				}
				else
					transform.position = Vector3.Lerp(oldState.position, newState.position,
						currentSmooth/(interval));
				//under review:
				//assume we lost a packet, move to newer state
				if(currentSmooth >= interval * 1.1){
					if(states.Count > 2){
						Debug.Log("moving on");
						states.DiscardOldest();
						currentSmooth = 0f;
					}
				}

			}
		}
	}

	bool modifyLerpTime = false;
	void OnSerializeNetworkView(BitStream stream, NetworkMessageInfo info)
	{
		Vector3 syncPosition = Vector3.zero;
		bool syncFacing = false;
		if (stream.isWriting)
		{
			syncFacing = facingRight;
			syncPosition = transform.position;
			stream.Serialize(ref syncPosition);
			stream.Serialize(ref syncFacing);
		}
		else
		{
			//reject out of order/duplicate packets
			//not sure if this can verify that a packet was lost yet;
			if(states.Count >= 3){
				canStart = true;
				double newestTime = states.ReadNewest().remoteTime;
				if(info.timestamp > newestTime + 1f/Network.sendRate * 1.5f){
					Debug.Log("lost previous packet");
					Debug.Log("local" + newestTime + "server" + info.timestamp);
					modifyLerpTime = true;
				}
				else if(info.timestamp < newestTime) {
					Debug.Log("out of order packet");
					return;
				}
				else if(info.timestamp == newestTime){
					Debug.Log("duplicate packet");
					return;
				}
			}

			//The period of time spent without an update.
			if(!modifyLerpTime)
				syncDelay = Time.time - lastSynctime;
			else{
				syncDelay = syncDelay + (Time.time - lastSynctime);
				modifyLerpTime = false;
			}

			currentSmooth = 0f; //reset period of interpolation, since we got new packet

			stream.Serialize(ref syncPosition);
			stream.Serialize(ref syncFacing);
			State state = new State(syncPosition, syncFacing);
			state.remoteTime = (float)info.timestamp;
			state.localTime = Time.time;
			states.Add(state); //if we advanced buffer manually, then count < maxsize

			lastSynctime = Time.time;
			/* if(syncFacing != facingRight)
				Turn(); */
		}
	}

	void OnCollisionEnter2D(Collision2D collision){
		if(collision.gameObject.tag  == "Bullet"){
			Projectile info = collision.gameObject.GetComponent<Projectile>();
			if(theOwner == info.theOwner)
				return;
			Destroy(collision.gameObject);
			SpriteRenderer myRenderer = gameObject.GetComponent<SpriteRenderer>();
			//iTween.ColorTo(myRenderer.gameObject, iTween.Hash("r", 200.0f, "time", 1.0f, "looptype", "pingpong"));
			myRenderer.color = ColorHSV.GetRandomColor(UnityEngine.Random.Range(0.0f, 360f), 1f, 1f);
		}
	}
}
