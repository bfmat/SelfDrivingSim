using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Car : MonoBehaviour {

	[SerializeField] [Range(-1f, 1f)] float steeringAngle;
	[SerializeField] [Range(0f, 16f)] float torque;

	[SerializeField] WheelCollider[] driveWheels;

	void FixedUpdate () {
		var steeringAngleDegrees = 90f * steeringAngle;
		foreach (var driveWheel in driveWheels) {
			driveWheel.steerAngle = steeringAngleDegrees;
			driveWheel.motorTorque = torque;
		}
	}
}
