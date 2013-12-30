using UnityEngine;
using System.Collections;
using System;

public class ManageInstances : MonoBehaviour {

	Player myPlayer;
	public Transform characterPrefab;
	void SpawnPlayer(NetworkPlayer player)
	{
		string tempPlayerString = player.ToString();
		int playerNumber = Convert.ToInt32(tempPlayerString);
		Transform newPlayerTransform = (Transform)Network.Instantiate(characterPrefab, transform.position, Quaternion.identity, playerNumber);
		myPlayer = newPlayerTransform.GetComponent<Player>();
		NetworkView theNetworkView = newPlayerTransform.networkView;
		theNetworkView.RPC("SetPlayerID", RPCMode.AllBuffered, player);
	}

	void OnServerInitialized()
	{
		SpawnPlayer(Network.player);
	}

	void OnConnectedToServer()
	{
		SpawnPlayer(Network.player);
	}
	void OnDisconnectedFromServer(){
		Network.RemoveRPCs(Network.player);
		Network.DestroyPlayerObjects(Network.player);
		Network.Destroy(myPlayer.gameObject);
	}
}
