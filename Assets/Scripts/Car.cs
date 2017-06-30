using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Car : MonoBehaviour {
	
	const float forwardTorquePerFixedUpdate = 1f;

	[SerializeField] GameObject castor;

	Vector3 castorDelta;
	Rigidbody driveAxleRigidbody;

	void Start () {
		driveAxleRigidbody = GetComponent<Rigidbody> ();
		castorDelta = castor.transform.position - transform.position;
	}

	void FixedUpdate () {
		driveAxleRigidbody.AddRelativeTorque (new Vector3 (0f, forwardTorquePerFixedUpdate, 0f));
		castor.transform.position = transform.position + castorDelta;
	}
}
