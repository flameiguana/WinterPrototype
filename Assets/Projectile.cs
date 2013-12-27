using UnityEngine;
using System.Collections;

public class Projectile : MonoBehaviour {
	public NetworkPlayer theOwner {get; set;}

	public void Start(){
		TrailRenderer trail = gameObject.GetComponent<TrailRenderer>();
		trail.sortingLayerName = "Character";
	}
	public void OnTriggerExit2D(Collider2D collider){
		Destroy (gameObject);
	}
}
