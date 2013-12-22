using UnityEngine;
using System.Collections;

public class PlayerMovement : MonoBehaviour {

	//make this nullable
	public NetworkPlayer? theOwner;
	int physicsStep = 0;
	int cachedSteps = 0;
	public bool DEBUG;
	float width;

	Vector3 physicsPosition;
	Vector3 authorizedPosition;
	/*The physics state
	struct State{
		Vector3 velocity;
		Vector3 position;
	}

	struct Move{
		float time;
		//The input
		Vector3 axes;
		State state;
	}
	*/

	Vector3 serverCurrentAxes, lastClientAxes;

	void Start(){
		SpriteRenderer myRenderer = gameObject.GetComponent<SpriteRenderer>();
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
		float deltaTime = (inputPhysStep - physicsStep) * Time.fixedDeltaTime;
		StepPhysics(serverCurrentAxes, deltaTime);
		physicsStep = inputPhysStep;
		//Don't use simulate latest move because the client hasn't simulated it yet. Just keep a copy.
		serverCurrentAxes = axes;

	}

	//In the future this function would handle collisions and gravity.
	void StepPhysics(Vector3 axes, float deltaTime){
		//This is the physics simulation part
		float speed = 2.0f;
		physicsPosition = physicsPosition + axes * speed * deltaTime;
	}

	// TODO: If we add more buttons, put them on queue to be sent on next frame
	void FixedUpdate () {
	 //We make sure the player associated with this object controls input
		//serverCurrentAxes = ;
		if (theOwner != null && Network.player.Equals(theOwner) && !DEBUG){
			Vector3 axes = new Vector3(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"), 0);
			if(axes != lastClientAxes || cachedSteps >= 3){
				lastClientAxes = axes;
				if (Network.isClient)
				{
					//Needs permission to move. Note this sends a move when we let go of button
					//as well
					networkView.RPC("SendNewInput", RPCMode.Server, axes, physicsStep);
				}
				cachedSteps = 0;
			}
			//Client side prediction. The server player (if we keep it, maintains his own position as accurate)
			StepPhysics(axes, Time.fixedDeltaTime);
			transform.position = physicsPosition;
			physicsStep++;
			cachedSteps++;
		}

		/* Smooth player movement for host's view*/
		else if(Network.isServer && !DEBUG){ //server side smoothing
			Vector3 positionDifference = physicsPosition - transform.position;
			float distanceApart = positionDifference.magnitude;
			if(distanceApart < width/24.0f)
				transform.position = physicsPosition;
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
			//TODO: find a more consistent smoothing functions
		}
	}

	void OnSerializeNetworkView(BitStream stream, NetworkMessageInfo info){
		//given that the client didn't instantiate the object, they will never be the one writing.
		// it is always the host
		if(stream.isWriting){
			Vector3 myPos = physicsPosition;
			stream.Serialize(ref myPos);
		}
		else
		{
			/*clients receive authoritative position*/
			authorizedPosition = Vector3.zero;
			stream.Serialize(ref authorizedPosition);
		}
	}
}
