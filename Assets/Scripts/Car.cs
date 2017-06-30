using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Car : MonoBehaviour {
	
	const float averageTorque = 10f;

	[SerializeField] [Range(-2, 2)] float steeringAngle;

	[SerializeField] WheelCollider rightWheel, leftWheel;

	void FixedUpdate () {
		var leftSpeedMultiplier = Mathf.Exp ((float)steeringAngle);
		var rightWheelTorque = averageTorque / (1 + leftSpeedMultiplier);
		rightWheel.motorTorque = rightWheelTorque;
		leftWheel.motorTorque = averageTorque - rightWheelTorque;
	}
}
