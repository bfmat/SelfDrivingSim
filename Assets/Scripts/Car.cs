using UnityEngine;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;

public class Car : MonoBehaviour {
	
	const float steeringAngleMultiplier = 0.1f;
	const float minSteeringBump = 0.005f;
	const float torque = 20f;
	const int resWidth = 200, resHeight = 150;

	[SerializeField] DrivingMode drivingMode;
	[SerializeField] WheelCollider[] driveWheels;
	[SerializeField] bool enableWheel;

	float steeringAngle = 0f;
	List<string> labels = new List<string> ();
	bool currentlyRecording;

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

		currentlyRecording = !(drivingMode == DrivingMode.Recording);
	}

	void FixedUpdate () {
		if (drivingMode != DrivingMode.Autonomous) {
			if (enableWheel) {
				steeringAngle = Input.GetAxis ("Steering");
			} else {
				if (Input.GetKey (KeyCode.A))
					steeringAngle -= minSteeringBump;
				if (Input.GetKey (KeyCode.D))
					steeringAngle += minSteeringBump;
			}
		}

		var steeringAngleDegrees = 90f * steeringAngle * steeringAngleMultiplier;
		foreach (var driveWheel in driveWheels) {
			driveWheel.steerAngle = steeringAngleDegrees;
			driveWheel.motorTorque = torque;
		}

		var recordingButton = Input.GetAxisRaw ("EnableRecording");
		if (recordingButton > 0)
			currentlyRecording = true;
		else if (recordingButton < 0)
			currentlyRecording = false;
		
#if UNITY_EDITOR_WIN
		print (currentlyRecording);
#else
		print (Time.timeSinceLevelLoad);
#endif
	}

	IEnumerator RecordFrame () {
		var camera = GetComponentInChildren<Camera> ();
		for (uint i = 0; true; i++) {
			do {
				yield return new WaitForEndOfFrame ();
			} while (!currentlyRecording);
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
#if UNITY_EDITOR_WIN
			var filename = "sim/" + i + ".png";
#else
			var filename = "/tmp/temp.png";
#endif
			File.WriteAllBytes(filename, bytes);
#if UNITY_EDITOR_LINUX
			File.Move ("/tmp/temp.png", "/tmp/sim" + i + ".png");
#endif
			labels.Add (filename + "," + steeringAngle.ToString ("F7"));
			yield return new WaitForEndOfFrame ();
		}
	}

	IEnumerator HandleAutonomousSteering () {
		while (true) {
			var maxIndex = -1;
			foreach (var path in Directory.GetFiles("/tmp")) {
				if (path.Contains ("sim.txt")) {
					var index = int.Parse(path.Substring (5, path.Length - 12));
					if (index > maxIndex)
						maxIndex = index;
				}
			}
			var streamReader = new StreamReader ("/tmp/" + maxIndex + "sim.txt");
			var fileContents = streamReader.ReadToEnd ();
			steeringAngle = float.Parse (fileContents);
			yield return null;
		}
	}

	void WriteLabels () {
		var labelsArray = labels.ToArray ();
		File.WriteAllLines ("sim/labels.csv", labelsArray);
	}

	enum DrivingMode {
		Recording,
		Autonomous,
		Manual
	}
}
