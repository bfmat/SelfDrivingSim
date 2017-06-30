using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Car : MonoBehaviour {
	
	const float forwardTorquePerFixedUpdate = 1f;

	WheelCollider[] driveWheels;

	void Start () {
		driveWheels = GetComponentsInChildren<WheelCollider> ();
	}

	void FixedUpdate () {
		foreach (var driveWheel in driveWheels) {
			driveWheel.motorTorque = 150000f;
		}
	}
}
