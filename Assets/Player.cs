using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;

//Under Consideration:
//1. Sync positions to server and then to clients (this relieves most players except host)

public class Player : MonoBehaviour {

	public bool DEBUG;
	public ManageInstances instances;

	public Rigidbody2D pellet;
	NetworkPlayer theOwner;
	public int playerNumber;
	bool isOwner = false;
	/* Movement variables */
	bool onGround = false;
	bool jump = false;
	bool facingRight = true;
	//bool canShoot = true;

	float width;
	const float groundRadius = 0.2f;
	public Transform groundCheck;
	public Transform mouth;
	public LayerMask ground;
	public float JUMP_FORCE;
	public float SHOOT_VELOCITY;

	/* Synchronization Variables */                                                                                                                                                                                                                                      
	private float currentSmooth =0f;
	private bool canInterpolate= false;
	float simTime;


	/*
	 * Holds various properties required to simulate the state of a player locally.
	 */
	struct State{
		public Vector3 position;
		public bool facingRight;
		public float remoteTime; //the time of this state on the client
		public float localTime;  //the time we received a copy of this state.
		public State(Vector3 position, bool facingRight){
			this.position = position;
			this.facingRight = facingRight;
			this.remoteTime = float.NaN;
			this.localTime = float.NaN;
		}
	}

	/*
	 * Stores parameters and a function name so that it can be called at a later time.
	 * Parameters can be of any type, so casting will be needed.
	 */
	public class Event{
		public string functionName;
		public float timestamp;
		public ArrayList parameters;
		public Event(){
			parameters = new ArrayList();
		}
	}
	//A priority queue of events sorted by earliest time to highest.
	private SortedList<float, Event> eventQueue;

	//A buffer of states. Not sure if a circular buffer is the best data structure at this point.
	CircularBuffer<State> states;


	//Sets the network ID to this instantiation of the player.
	[RPC]
	void SetPlayerID(NetworkPlayer player)
	{
		theOwner = player;
		if(player == Network.player){ 
			isOwner = true; //we can control the player locally
			rigidbody2D.isKinematic = false;
		}
	}

	void Awake()
	{
		//Since most motion is directed by interpolation, don't allow local forces to move this player.
		rigidbody2D.isKinematic = true;

		states = new CircularBuffer<State>(4);
		eventQueue = new SortedList<float, Event>();
	}

	// Use this for initialization
	void Start ()
	{
		//get references for existing objects
		instances = GameObject.Find ("InstanceManager").GetComponent<ManageInstances>(); //meh
		BoxCollider2D collider = gameObject.GetComponent<BoxCollider2D>();
		width = collider.size.x;
		//lerpDelay = 2f / Network.sendRate; //with tick rate 25, and delay of 2 frames, we have .08s delay
	}


	[RPC]
	void ShootNotify(int facingRight, Vector3 mouthPosition, NetworkMessageInfo info)
	{
		//This variable stores the delay from sender to here.
		float requestDelta =  (float)(Network.time - info.timestamp); 	
		//shoot from where the player currently is (this may not look right because position on server is interpolated)
		networkView.RPC("Shoot", RPCMode.Others, facingRight, mouthPosition, requestDelta);

		/*We know that only the server calls shootRequest, so we can call the following without
		 * checking if we're on the server. Since times in info and original time are the same, lag
		 * will be less than on client b*/
		Shoot (facingRight, mouthPosition,requestDelta, info);
	}
	
	//The server tells the clients to call this function, which eventually calls ShootLocal
	[RPC]
	void Shoot(int facingRight, Vector3 mouthPosition, float requestDelta,  NetworkMessageInfo info){
		if(isOwner)
			return; //reject the message because you already did it
		float originalTime = (float)info.timestamp - requestDelta;
		if(originalTime > simTime){
			/*
			 * Sacrifice realness for less lag and just shoot instantly
			Event doLater = new Event();
			doLater.functionName = "Shoot";
			doLater.timestamp = originalTime;
			doLater.parameters.Add(facingRight);
			doLater.parameters.Add(mouthPosition);
			//doLater.parameters.Add(info);
			eventQueue.Add(doLater.timestamp, doLater);
			*/
			ShootLocal (facingRight, mouthPosition, (float)Network.time - originalTime);
		}
		else
			Debug.Log ("Too late. Local: " + simTime + " Remote: " + originalTime);
	}

	//The function that actually spawns the projectile.
	void ShootLocal(int facingRight, Vector3 mouthPosition, float lag){
		float flip = 1f;
		if(facingRight != 1)
			flip = -1f;
		//Teleport a certain distance apart depending on lag.
		//In the real game we would just speed up rocket and/or reduce the animation duration for shooting.
		float distanceApart  = lag * SHOOT_VELOCITY;
		mouthPosition.x = mouthPosition.x + flip * (distanceApart * 2f);
		Rigidbody2D projectile = (Rigidbody2D)Instantiate (pellet, mouthPosition, Quaternion.identity);
		instances.projectiles[playerNumber] =  projectile.gameObject;
		Projectile script = projectile.GetComponent<Projectile>();
		script.theOwner = theOwner;
		projectile.velocity = (new Vector2(SHOOT_VELOCITY*flip, 0f));
	}


	//This function is sent to everyone but the server and tells them to store a hit event.
	[RPC]
	void NotifyHit(float r, float g, float b, int projectileID, NetworkMessageInfo info){
		Color color = new Color(r, g, b);
		Event doLater = new Event();
		doLater.functionName = "Hit";
		doLater.timestamp = (float)info.timestamp;
		doLater.parameters.Add (color);
		doLater.parameters.Add (projectileID);
		eventQueue.Add(doLater.timestamp, doLater);
		if(doLater.timestamp > simTime)
			Debug.Log ("I'm dumber");
		//Hit (color, projectileID);
	}

	//Change color to match that on the server. This could be extended to do a variety of things.
	void Hit(Color color, int projectileID){
		SpriteRenderer myRenderer = gameObject.GetComponent<SpriteRenderer>();
		myRenderer.color = color;
		//Destroys the bullet, identified by the ID.
		Destroy (instances.projectiles[projectileID]);
		instances.projectiles[projectileID] = null;
	}


	void Turn(){
		facingRight = !facingRight;
		Vector3 localScale = transform.localScale;
		localScale.x *= -1;
		transform.localScale = localScale;
	}

	void FixedUpdate()
	{
		if (isOwner){
			//If you're the owner you can jump and move and stuff.
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

		//Process 1 event. You don't have to be the owner
		if(eventQueue.Count != 0){
			//If this instance is being simulated, use simTime, otherwise use real time.
			float relativeTime = isOwner ? (float)Network.time : simTime;
			if(eventQueue.Keys[0] <= relativeTime){
				Event doNow = eventQueue.Values[0];
				eventQueue.RemoveAt(0);
				//Debug.Log ("Local: " + Network.time + " Remote: " + doNow.timestamp);
				if(doNow.functionName == "Shoot")
					ShootLocal ((int)doNow.parameters[0], (Vector3)doNow.parameters[1], ((float)Network.time - doNow.timestamp));
				if(doNow.functionName == "Hit")
					Hit ((Color)doNow.parameters[0], (int)doNow.parameters[1]);
			}
		}

	}

	float interval = float.PositiveInfinity;
	//Update is called once per frame
	void Update ()
	{
		if(isOwner){
			//A jump command is only detected once per frame.
			if(onGround && Input.GetKeyDown(KeyCode.Space)){
				jump = true;
			}
			//If you press the key and there isn't already a projectile then you can shoot
			if(instances.projectiles[playerNumber] == null && Input.GetKeyDown(KeyCode.Q)){
				if(Network.isServer){
					networkView.RPC("Shoot", RPCMode.Others, Convert.ToInt32(facingRight), mouth.position, 0f); //0 network delay
				}
				else{
					networkView.RPC("ShootNotify", RPCMode.Server, Convert.ToInt32(facingRight), mouth.position);
				}
					//IMPORTANT: For now, we are spawning a projectile for clients and server.
					//Depending on testing, we may want to ask for permission for shooting from the server.
					ShootLocal(Convert.ToInt32(facingRight), mouth.position, 0f);
			}
		}
		else{
			//check if we have enough states to interpolate between.
			if(canInterpolate){
				currentSmooth += Time.deltaTime;

				//if we go past these two states, move to next one.
				if(currentSmooth >= interval){
					if(states.Count > 2){
						states.DiscardOldest();
						//if we were perfectly in sync, we could reset to 0, but instead set it to the amount we overshot
						currentSmooth = currentSmooth - interval; 
					}
					else {
						Debug.Log("Missed too many packets. We need 2 states to interpolate between.");
						currentSmooth = 0f;
						interval = float.PositiveInfinity;
						canInterpolate = false;
						//don't interpolate but let gravity do its job so that players dont freeze in air
						rigidbody2D.isKinematic = false;
					}
				}
				//read the two oldest states.
				State oldState = states.ReadOldest(); 
				State newState = states.GetByIndex(1);

				//tells us how long to interpolate between these two states.
				interval = newState.remoteTime - oldState.remoteTime;

				if(newState.facingRight != facingRight)
					Turn();

				Vector3 positionDifference = newState.position - oldState.position;
				float distanceApart = positionDifference.magnitude;
				if(distanceApart > width * 20.0f){
					//snap to position if difference is too great.
					transform.position = newState.position;
					oldState.position = newState.position;
				}
				else
					transform.position = Vector3.Lerp(oldState.position, newState.position,
						currentSmooth/(interval));
				//The local simulation time of this thing.
				simTime = oldState.remoteTime +  currentSmooth;
			}
		}
	}
	
	void OnSerializeNetworkView(BitStream stream, NetworkMessageInfo info)
	{
		Vector3 syncPosition = Vector3.zero;
		bool syncFacing = false;

		if (stream.isWriting)
		{
			//if we have control over this entity, send out our positions to everyone else.
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

			if(canInterpolate == false && states.Count >= 3){
				rigidbody2D.isKinematic = true;
				canInterpolate = true;
			}

			stream.Serialize(ref syncPosition);
			stream.Serialize(ref syncFacing);
			State state = new State(syncPosition, syncFacing);
			state.remoteTime = (float)info.timestamp;
			state.localTime = (float)Network.time;
			states.Add(state); //if we advanced buffer manually, then count < maxsize
		}
	}

	//Collision for triggers
	void OnTriggerEnter2D(Collider2D other){
		if(other.gameObject.tag  == "Bullet"){
			Projectile info = other.gameObject.GetComponent<Projectile>();

			//if its our own ignore it.
			if(theOwner == info.theOwner)
				return;

			//If this collision happens on the server, acknowledge it and tell the clients.
			if(Network.isServer){
				Color color = ColorHSV.GetRandomColor(UnityEngine.Random.Range(0.0f, 360f), 1f, 1f);
				int projectileID = instances.projectiles.IndexOf(info.gameObject);
				networkView.RPC("NotifyHit", RPCMode.Others, color.r, color.g, color.b, projectileID);
				Hit(color, projectileID);
			}
		}
	}
}
