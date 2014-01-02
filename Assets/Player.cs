using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

//TODO:
//1. Sync positions to server and then to clients (this relieves most players except host)
//2. Confirm hit on server (majority vote?, trust 1 client?)
//enhance the realism of attacks at the expense of the realism of taking damage. However, this does not
//apply to rockets or melee
public class Player : MonoBehaviour {

	public bool DEBUG;

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
	public float SHOOT_VELOCITY;

	/* Synchronization Variables */                                                                                                                                                                                                                                      
	private float currentSmooth =0f;
	private bool canStart= false;
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

	private Queue<Event> eventQueue;
	public class Event{
		public string functionName;
		public float timestamp;
		public ArrayList parameters;
		public Event(){
			parameters = new ArrayList();
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
	void ShootRequest(int facingRight, Vector3 mouthPosition, NetworkMessageInfo info)
	{
		float requestDelta =  (float)(Network.time - info.timestamp); 	//This variable stores the delay from sender to here.
		//shoot from where the player currently is (this might still be buggy because position on server is interpolated)
		networkView.RPC("Shoot", RPCMode.Others, facingRight, mouthPosition, requestDelta);
		//We know that only the server reads this function, so we can call the following without checking
		//if we're on the server
		//since times in info and original time are the same, lag will be less than on client b
		Shoot (facingRight, mouthPosition,requestDelta, info);
	}
	
	//To players (and host client)
	[RPC]
	void Shoot(int facingRight, Vector3 mouthPosition, float requestDelta,  NetworkMessageInfo info){
		if(isOwner)
			return; //reject the message because you already did it
		float originalTime = (float)info.timestamp - requestDelta;
		if(originalTime > simTime){
			//TODO: store lag ino order to push projectile forwards to sync with player who launched
			//Debug.Log("Storing Event");
			Event doLater = new Event();
			doLater.functionName = "Shoot";
			doLater.timestamp = originalTime;
			doLater.parameters.Add(facingRight);
			doLater.parameters.Add(mouthPosition);
			//doLater.parameters.Add(info);
			eventQueue.Enqueue(doLater);
			return;
		}
		else
			Debug.Log ("Too late. Local: " + simTime + " Remote: " + originalTime);
	}

	//The function that actually spawns the rocket.
	void ShootLocal(int facingRight, Vector3 mouthPosition, float lag){

		float flip = 1f;
		if(facingRight != 1)
			flip = -1f;
		//teleport. In teh real game we would just speed up rocket
		float distanceApart  = lag * SHOOT_VELOCITY;
		//Debug.Log("lag: " + lag); 
		mouthPosition.x = mouthPosition.x + flip * (distanceApart * 2f);
		Rigidbody2D projectile = (Rigidbody2D)Instantiate (pellet, mouthPosition, Quaternion.identity);
		Projectile script = projectile.GetComponent<Projectile>();
		script.theOwner = theOwner;
		//try doing a constant velocity for better look/to make this relevant to grappling hook

		projectile.velocity = (new Vector2(SHOOT_VELOCITY*flip, 0f));
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
		states = new CircularBuffer<State>(4);
		eventQueue = new Queue<Event>();
	}
	// Use this for initialization
	void Start ()
	{
		BoxCollider2D collider = gameObject.GetComponent<BoxCollider2D>();
		width = collider.size.x;
		//localPosition = transform.position;
		//lerpDelay = 2f / Network.sendRate; //with tick rate 25, and delay of 2 frames, we have .08s delay
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
			//Process 1 event
			if(eventQueue.Count != 0){
				if(DEBUG || eventQueue.Peek().timestamp <= simTime){
					Event doNow = eventQueue.Dequeue();
					//Debug.Log ("Local: " + Network.time + " Remote: " + doNow.timestamp);
					if(doNow.functionName == "Shoot")
						ShootLocal ((int)doNow.parameters[0], (Vector3)doNow.parameters[1], ((float)Network.time - doNow.timestamp));
				}
			}
		}
	}

	float interval = float.PositiveInfinity;
	// Update is called once per frame
	void Update ()
	{
		if(isOwner){
			if(onGround && Input.GetKeyDown(KeyCode.Space)){
				jump = true;
			}
			if(canShoot && Input.GetKeyDown(KeyCode.Q)){
				if(Network.isServer){
					networkView.RPC("Shoot", RPCMode.Others, Convert.ToInt32(facingRight), mouth.position, 0f); //0 network delay
				}
				else{
					networkView.RPC("ShootRequest", RPCMode.Server, Convert.ToInt32(facingRight), mouth.position);
				}
					ShootLocal(Convert.ToInt32(facingRight), mouth.position, 0f);
			}
		}
		else{
			if(canStart){
				//Debug.Log(interval);
				currentSmooth += Time.deltaTime;
				//under review:
				//assume we lost a packet, move to newer state
				if(currentSmooth >= interval){
					if(states.Count > 2){
						states.DiscardOldest();
						//if we were perfectly in sync, we could reset to 0, but instead set it to the amount
						//we overshot
						currentSmooth = currentSmooth - interval; 
					}
					else {
						Debug.Log("missed too many packets");
						currentSmooth = 0f;
						interval = float.PositiveInfinity;
						canStart = false;
						//don't interpolate but let gravity do its job so that players dont freeze in air
						rigidbody2D.isKinematic = false;
					}
				}
				//read the two oldest states.
				State oldState = states.ReadOldest(); //TODO add code for packet loss
				State newState = states.ReadAt(1);

				interval = newState.remoteTime - oldState.remoteTime;

				if(newState.facingRight != facingRight)
					Turn();
				//Vector3 positionDifference = serverPosition - localPosition;
				Vector3 positionDifference = newState.position - oldState.position;
				float distanceApart = positionDifference.magnitude;
				if(distanceApart > width * 20.0f){
					transform.position = newState.position;
					oldState.position = newState.position;
				}
				else
					transform.position = Vector3.Lerp(oldState.position, newState.position,
						currentSmooth/(interval));
				simTime = oldState.remoteTime +  currentSmooth; // this might be risky if we skip a frame
			}
		}
	}
	
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
			if(states.Count >= 2){
				double newestTime = states.ReadNewest().remoteTime;
				if(info.timestamp >= newestTime + 1f/Network.sendRate * 2.0f){
					Debug.Log("lost previous packet");
					Debug.Log("local" + newestTime + "server" + info.timestamp);
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
			if(canStart == false && states.Count >= 3){
				rigidbody2D.isKinematic = true;
				canStart = true;
			}

			stream.Serialize(ref syncPosition);
			stream.Serialize(ref syncFacing);
			State state = new State(syncPosition, syncFacing);
			state.remoteTime = (float)info.timestamp;
			state.localTime = (float)Network.time;
			states.Add(state); //if we advanced buffer manually, then count < maxsize
		}
	}

	void ApplyDamage(){

	}
	void OnTriggerEnter2D(Collider2D other){
		if(other.gameObject.tag  == "Bullet"){
			Projectile info = other.gameObject.GetComponent<Projectile>();
			if(theOwner == info.theOwner)
				return;

			Destroy(other.gameObject);
			SpriteRenderer myRenderer = gameObject.GetComponent<SpriteRenderer>();
			//iTween.ColorTo(myRenderer.gameObject, iTween.Hash("r", 200.0f, "time", 1.0f, "looptype", "pingpong"));
			myRenderer.color = ColorHSV.GetRandomColor(UnityEngine.Random.Range(0.0f, 360f), 1f, 1f);
		}
	}
}
