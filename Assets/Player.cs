using UnityEngine;
using System.Collections;
using System;

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
	Vector3 localPosition;
	Vector3 serverPosition;
	private float smoothInterval;
	private float syncDelay;
	private float lastSynctime;



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
	void ShootRequest(int facingRight)
	{
		networkView.RPC("ShotFired", RPCMode.All, facingRight);
	}

	//To players (and host client)
	[RPC]
	void ShotFired(int facingRight){

		Rigidbody2D projectile = (Rigidbody2D)Instantiate (pellet, mouth.position, Quaternion.identity);
		Projectile info = projectile.GetComponent<Projectile>();
		info.theOwner = theOwner;
		//try doing a constant velocity for better look
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
	}
	// Use this for initialization
	void Start ()
	{
		BoxCollider2D collider = gameObject.GetComponent<BoxCollider2D>();

		width = collider.size.x;
		Debug.Log(width);
		localPosition = transform.position;
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
	}

	// Update is called once per frame
	void Update ()
	{
		if(isOwner){
			if(onGround && Input.GetKeyDown(KeyCode.Space)){
				jump = true;
			}
			if(canShoot && Input.GetKeyDown(KeyCode.Q)){
				networkView.RPC("ShootRequest", RPCMode.Server, Convert.ToInt32(facingRight));
			}
		}
		else
		{
			smoothInterval += Time.deltaTime;
			Vector3 positionDifference = serverPosition - localPosition;
			float distanceApart = positionDifference.magnitude;
			if(distanceApart > width * 5.0f){
				transform.position = serverPosition;
				localPosition = serverPosition;
			}
			else
				transform.position = Vector3.Lerp(localPosition, serverPosition, smoothInterval/syncDelay);
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
			stream.Serialize(ref syncPosition);
			stream.Serialize(ref syncFacing);
			smoothInterval = 0f;
			//shold remain constant unless we change the sync rate or miss a packet
			syncDelay = Time.time - lastSynctime;
			lastSynctime = Time.time;

			localPosition = transform.position;
			serverPosition = syncPosition;

			if(syncFacing != facingRight)
				Turn();
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
