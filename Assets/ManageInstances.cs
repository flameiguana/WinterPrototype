using UnityEngine;
using System.Collections;
using System;

public class ManageInstances : MonoBehaviour {

	public Transform characterPrefab;
	void SpawnPlayer(NetworkPlayer player)
	{
		string tempPlayerString = player.ToString();
		int playerNumber = Convert.ToInt32(tempPlayerString);
		Transform newPlayerTransform = (Transform)Network.Instantiate(characterPrefab, transform.position, Quaternion.identity, playerNumber);
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

}
