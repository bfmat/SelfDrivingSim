using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Car : MonoBehaviour {

	[SerializeField] [Range(-1f, 1f)] float steeringAngle;
	[SerializeField] [Range(0f, 16f)] float torque;

	[SerializeField] WheelCollider[] driveWheels;

	void Start () {
		foreach (var wheel in GetComponentsInChildren<WheelCollider>()) {
			wheel.ConfigureVehicleSubsteps (5f, 12, 15);
		}
	}

	void FixedUpdate () {
		var steeringAngleDegrees = 90f * steeringAngle;
		foreach (var driveWheel in driveWheels) {
			driveWheel.steerAngle = steeringAngleDegrees;
			if (GetComponent<Rigidbody> ().velocity.magnitude < 3)
				driveWheel.motorTorque = torque;
			else
				driveWheel.motorTorque = torque / 2;
		}
	}

	void GenerateWaypoint () {
		var waypoint = new GameObject ();
		waypoint.name = "WP";
		waypoint.transform.position = transform.position;
	}
}
