using UnityEngine;
using System.Collections;
using System;

public class InstanceManager : MonoBehaviour {

	public Transform characterPrefab;
	public ArrayList playerScripts = new ArrayList(); //might use later


	void SpawnPlayer(NetworkPlayer player)
	{
		string tempPlayerString = player.ToString();
		int playerNumber = Convert.ToInt32(tempPlayerString);

		Transform newPlayerTransform = (Transform)Network.Instantiate(characterPrefab, transform.position, transform.rotation, playerNumber);

		playerScripts.Add(newPlayerTransform.GetComponent<PlayerMovement>()); 
		NetworkView theNetworkView = newPlayerTransform.networkView;
		//tell everyone to call setPlayer on this copy of the network view including those who join late
		theNetworkView.RPC("SetPlayer", RPCMode.AllBuffered, player);
	}

	void OnServerInitialized()
	{
		SpawnPlayer(Network.player);
	}

	//This function is only callled on the server, so it is the owner
	void OnPlayerConnected(NetworkPlayer player){
		Debug.Log("Player connected from " + player.ipAddress + ":" + player.port);
		SpawnPlayer(player);
	}

	void OnPlayerDisconnected(NetworkPlayer player){
		int index = 0;
		foreach(PlayerMovement playerScript in playerScripts){
			index++;
			if(playerScript.theOwner == player){
				Network.RemoveRPCs(player);
				Network.DestroyPlayerObjects(player);
				Network.Destroy(playerScript.gameObject);
			}
		}
		playerScripts.RemoveAt(index);
	}
	

	/*
	 * Here. the player would spawn his own character
	void OnConnectedToServer()
	{
		SpawnPlayer();
	}
	*/
}
