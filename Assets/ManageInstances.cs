using UnityEngine;
using System.Collections.Generic;
using System;

public class ManageInstances : MonoBehaviour {

	int playerCounter;
	//playerID, gameObject
	public List<GameObject> projectiles;

	void Awake(){
		playerCounter = -1;
		projectiles = new List<GameObject>(); 
	}

	Player myPlayer;
	public Transform characterPrefab;
	void SpawnPlayer(NetworkPlayer player)
	{
		string tempPlayerString = player.ToString();
		int playerNumber = Convert.ToInt32(tempPlayerString);
		Transform newPlayerTransform = (Transform)Network.Instantiate(characterPrefab, transform.position, Quaternion.identity, playerNumber);
		myPlayer = newPlayerTransform.GetComponent<Player>();
		myPlayer.playerNumber = playerCounter;
		NetworkView theNetworkView = newPlayerTransform.networkView;
		theNetworkView.RPC("SetPlayerID", RPCMode.AllBuffered, player);
	}

	[RPC] 
	void AddPlayer(){
		playerCounter++;
		projectiles.Add(null); //increase size of container to match player count
	}

	void OnServerInitialized()
	{
		networkView.RPC("AddPlayer", RPCMode.AllBuffered);
		SpawnPlayer(Network.player);
	}

	void OnConnectedToServer()
	{
		networkView.RPC("AddPlayer", RPCMode.AllBuffered);
		SpawnPlayer(Network.player);
	}
	void OnDisconnectedFromServer(){
		Network.RemoveRPCs(Network.player);
		Network.DestroyPlayerObjects(Network.player);
		Network.Destroy(myPlayer.gameObject);
	}
}
