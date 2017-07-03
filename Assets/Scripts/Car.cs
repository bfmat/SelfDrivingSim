using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Car : MonoBehaviour {

	[SerializeField] [Range(-1f, 1f)] float steeringAngle;

	[SerializeField] WheelCollider[] driveWheels;

	const float steeringAngleMultiplier = 0.1f;
	const float torque = 20f;

	void Start () {
		foreach (var wheel in GetComponentsInChildren<WheelCollider>()) {
			wheel.ConfigureVehicleSubsteps (5f, 12, 15);
		}
	}

	void FixedUpdate () {
		var steeringAngleDegrees = 90f * steeringAngle * steeringAngleMultiplier;
		foreach (var driveWheel in driveWheels) {
			driveWheel.steerAngle = steeringAngleDegrees;
			driveWheel.motorTorque = torque;
		}
	}

	void GenerateWaypoint () {
		var waypoint = new GameObject ();
		waypoint.name = "WP";
		waypoint.transform.position = transform.position;
	}
}
