using UnityEngine;
using System.Collections;

[RequireComponent(typeof(NetworkView))]
public class TimeManager : MonoBehaviour {

	public static TimeManager instance;
	private float deltaTime;
	public float time;
	
	void Awake(){
		instance = this;
		if(Network.isServer){
			deltaTime = -(float)Network.time;
		}
		else{
			networkView.RPC("GetServerTime",RPCMode.Server);
		}
	}
	void Update () {
		time = (float)Network.time + deltaTime;
	}

	[RPC]
	void GetServerTime(NetworkMessageInfo info){
		networkView.RPC("SetDeltaTime", info.sender, time); 
	} 
	
	[RPC]
	void SetDeltaTime (float serverTime, NetworkMessageInfo info)
	{
		deltaTime = serverTime - (float)info.timestamp; 
		Debug.Log("Delta " + deltaTime + "  serverTime =  " + serverTime.ToString()); 
	}
}
