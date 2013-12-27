using UnityEngine;
using System.Collections;

public class PlayerMovement : MonoBehaviour {

	State serverState;
	CircularBuffer<Move> moveBuffer;
	//make this nullable
	public NetworkPlayer? theOwner;
	int physicsStep = 0;
	int cachedSteps = 0;
	public bool DEBUG;
	float width;
	bool facingRight = true;
	bool onGround = false;
	bool jump = false;
	const float groundRadius = 0.2f;
	public Transform groundCheck;
	public LayerMask ground;
	public float JUMP_FORCE;
	Vector3 physicsPosition;
	Vector3 authorizedPosition;
	Vector3 authorizedVelocity;

	struct State{
		public Vector3 velocity;
		public Vector3 position;
		public int physicsStep;
	}

	struct Move{
		public Vector3 axes;
		public State state;
	}

	int serverCurrentStep;
	Vector3 serverCurrentAxes, lastClientAxes;

	void Start(){
		SpriteRenderer myRenderer = gameObject.GetComponent<SpriteRenderer>();
		moveBuffer = new CircularBuffer<Move>(60);
		serverState = new State();
		authorizedPosition = transform.position;
		physicsPosition = transform.position;
		width = myRenderer.bounds.size.x;
	}

	[RPC]
	void SetPlayer(NetworkPlayer player)
	{
		theOwner = player;
	}
	
	[RPC]
	void SendNewInput(Vector3 axes, int inputPhysStep)
	{
		//if time of action < server simulation time (handles out of order packets)
		if(inputPhysStep < physicsStep)
			return;
		//find how many physics steps were're behind and take that time into account
		float frames = inputPhysStep - physicsStep;
		StepPhysics(serverCurrentAxes, frames);
		physicsStep = inputPhysStep;
		//Don't use simulate latest move because the client hasn't simulated it yet. Just keep a copy.
		serverCurrentAxes = axes;

	}

	void Turn(){
		facingRight = !facingRight;
		Vector3 localScale = transform.localScale;
		localScale.x *= -1;
		transform.localScale = localScale;
	}


	//In the future this function would handle collisions and gravity.
	void StepPhysics(Vector3 axes, float frames){
		float speed = 6.0f; //6 units per second
		rigidbody2D.velocity = new Vector2(axes.x * speed * frames, rigidbody2D.velocity.y);
		if(axes.z > 0){
			rigidbody2D.AddForce(new Vector2(0, JUMP_FORCE));
			jump = false;
		}
		//physicsPosition = physicsPosition + axes * speed * deltaTime;
	}



	void ClientCorrection(){
		//Discard irrelevant moves
		while(serverState.physicsStep > moveBuffer.ReadOldest().state.physicsStep && !moveBuffer.IsEmpty())
			moveBuffer.DiscardOldest();
		if(!moveBuffer.IsEmpty() && moveBuffer.ReadOldest().state.physicsStep == serverState.physicsStep)
		{
			bool withinThreshold = false;
			if(withinThreshold){
				//correct our current state with what the server says is correct + our input since then
				//rewind
				physicsStep = serverState.physicsStep;
				transform.position = serverState.position;
				rigidbody2D.velocity = serverState.velocity;
				//assume our input is the same as it was on the server.
				//Vector3 input = moveBuffer.ReadOldest().axes;
				moveBuffer.DiscardOldest();

				//go from oldest move to current move
			//	foreach(Move move in moveBuffer){
					//here we would step physics and re apply moves up till the current one.
					//I don't know if this is possible or even efficient to do so in unity
			//	}
			}
		}
	}

	// TODO: If we add more buttons, put them on queue to be sent on next frame
	void FixedUpdate () {
	 //We make sure the player associated with this object controls input
		if (theOwner != null && Network.player.Equals(theOwner)){
			onGround = Physics2D.OverlapCircle(groundCheck.position , groundRadius, ground);
			float up = jump ? 1.0f : 0.0f;
			Vector3 axes = new Vector3(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"), up);
			if(axes != lastClientAxes || cachedSteps >= 3){
				lastClientAxes = axes;
				if (Network.isClient)
				{
					//store move locally
					Move move = new Move();
					move.state = new State();
					move.state.position = transform.position;
					move.state.velocity = rigidbody2D.velocity;
					move.axes = axes;
					move.state.physicsStep = physicsStep;
					moveBuffer.Add(move);
					//Needs permission to move. Note this sends a move when we let go of button as well
					networkView.RPC("SendNewInput", RPCMode.Server, axes, physicsStep);
				}
				cachedSteps = 0;
			}
			//Client side prediction. The server player (if we keep it, maintains his own position as accurate)
			StepPhysics(axes, 1.0f);
			if(facingRight && axes.x < 0 || !facingRight && axes.x > 0)
				Turn ();

			//transform.position = physicsPosition;
			physicsStep++;
			cachedSteps++;
		}

		/* Smooth player movement for host's view*/
		else if(Network.isServer && !DEBUG){ //server side smoothing
			Vector3 positionDifference = transform.position - physicsPosition;
			float distanceApart = positionDifference.magnitude;
			if(distanceApart > width*4.0f || distanceApart < width/24.0f)
				transform.position = transform.position;
			else
				transform.position += positionDifference * .1f;
		}
		/*Smooth movement of players if you're the client.
		 TODO: remove the else part so that we can also handle movement correction from server*/
		else if(Network.isClient){
			Vector3 positionDifference =  authorizedPosition - transform.position;
			float distanceApart = Vector3.Distance(authorizedPosition, transform.position);
			if(distanceApart > width*4.0f || distanceApart < width/24.0f)
				transform.position = authorizedPosition; //snap
			else
				transform.position += positionDifference * .1f; //smooth
			rigidbody2D.velocity = authorizedVelocity;
			//TODO: find a more consistent smoothing functions
		}
	}

	void Update(){
		if(onGround && Input.GetKeyDown(KeyCode.Space)){
			jump = true;
		}
		physicsPosition = transform.position;
	}
	
	void OnSerializeNetworkView(BitStream stream, NetworkMessageInfo info){
		//given that the client didn't instantiate the object, they will never be the one writing.
		// it is always the host
		if(stream.isWriting){
			Vector3 myPos = transform.position;
			stream.Serialize(ref myPos);
			Vector3 myVelocity = rigidbody2D.velocity;
			stream.Serialize(ref myVelocity);
			stream.Serialize (ref serverCurrentStep);
		}
		else
		{
			/*clients receive authoritative position*/
			authorizedPosition = Vector3.zero;
			stream.Serialize(ref authorizedPosition);

			//snap velocity
			authorizedVelocity = Vector3.zero;
			stream.Serialize(ref authorizedVelocity);

			int serverStep = 0;
			stream.Serialize(ref serverStep);

			serverState.position = authorizedPosition;
			serverState.velocity = authorizedVelocity;
			serverState.physicsStep = serverStep;
		}
	}
}
