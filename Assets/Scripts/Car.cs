using UnityEngine;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;

public class Car : MonoBehaviour {
	
	const DrivingMode drivingMode = DrivingMode.Autonomous;
	const float steeringAngleMultiplier = 0.1f;
	const float minSteeringBump = 0.005f;
	const float torque = 20f;
	const int resWidth = 360, resHeight = 240;

	[SerializeField] WheelCollider[] driveWheels;

	float steeringAngle = 0f;
	List<string> labels = new List<string> ();

	void Start () {
		foreach (var wheel in GetComponentsInChildren<WheelCollider>()) {
			wheel.ConfigureVehicleSubsteps (5f, 12, 15);
		}

		if (drivingMode != DrivingMode.Manual) {
			StartCoroutine (RecordFrame ());
			if (drivingMode == DrivingMode.Recording)
				InvokeRepeating ("WriteLabels", 1f, 1f);
			else
				StartCoroutine (HandleAutonomousSteering ());
		}
	}

	void FixedUpdate () {
		if (drivingMode != DrivingMode.Autonomous) {
			if (Input.GetKey (KeyCode.A))
				steeringAngle -= minSteeringBump;
			if (Input.GetKey (KeyCode.D))
				steeringAngle += minSteeringBump;
		} else {
			
		}

		var steeringAngleDegrees = 90f * steeringAngle * steeringAngleMultiplier;
		foreach (var driveWheel in driveWheels) {
			driveWheel.steerAngle = steeringAngleDegrees;
			driveWheel.motorTorque = torque;
		}
	}

	IEnumerator RecordFrame () {
		var camera = GetComponentInChildren<Camera> ();
		for (uint i = 0; true; i++) {
			var renderTexture = new RenderTexture(resWidth, resHeight, 24);
			camera.targetTexture = renderTexture;
			var screenShot = new Texture2D(resWidth, resHeight, TextureFormat.RGB24, false);
			camera.Render();
			RenderTexture.active = renderTexture;
			screenShot.ReadPixels(new Rect(0, 0, resWidth, resHeight), 0, 0);
			camera.targetTexture = null;
			RenderTexture.active = null; // JC: added to avoid errors
			Destroy(renderTexture);
			var bytes = screenShot.EncodeToPNG();
			var filename = "/tmp/sim" + i + ".png";
			File.WriteAllBytes(filename, bytes);
			labels.Add (filename + "," + steeringAngle.ToString ("F7"));
			yield return new WaitForEndOfFrame ();
		}
	}

	IEnumerator HandleAutonomousSteering () {
		while (true) {
			var streamReader = new StreamReader ("/tmp/sim");
			var fileContents = streamReader.ReadToEnd ();
			steeringAngle = float.Parse (fileContents);
			print (steeringAngle);
			yield return null;
		}
	}

	void WriteLabels () {
		var labelsArray = labels.ToArray ();
		File.WriteAllLines ("/home/brendon/sim/labels.csv", labelsArray);
	}

	enum DrivingMode {
		Recording,
		Autonomous,
		Manual
	}
}
