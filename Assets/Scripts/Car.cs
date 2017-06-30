using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Car : MonoBehaviour {
	
	const float forwardTorquePerFixedUpdate = 10f;

	[SerializeField] WheelCollider[] driveWheels;

	void FixedUpdate () {
		foreach (var driveWheel in driveWheels) {
			driveWheel.motorTorque = forwardTorquePerFixedUpdate;
		}
	}
}
