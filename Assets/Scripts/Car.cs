using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

sealed class Car : MonoBehaviour {
	
	const float steeringAngleMultiplier = 0.1f; // Scaling factor for the steering wheel input 
	const float minSteeringBump = 0.005f; // The amount to increase the steering angle per key press (for keyboard controls)
	const float torque = 16f; // Torque to constantly apply to the front wheels
	const float timeSpentOnLane = 100f; // During automated tests, spend this many seconds on each individual lane
	const int resWidth = 200, resHeight = 150; // Width and height of saved screenshots
	const string tmpPath = "/tmp/"; // Path to save images in during autonomous driving
	const bool useLowPassFilter = true; // Apply the low pass filter in Utility to steering angles in autonomous mode

	[SerializeField] DrivingMode drivingMode; // Manual, recording, autonomous, automated test
	[SerializeField] WheelCollider[] driveWheels; // The two front wheels
	[SerializeField] bool enableWheel; // Use a steering wheel to manually drive the car (I have only tested a Logitech G29)
	[SerializeField] bool drawLine; // Drop points behind the car (used for automated testing)
	[SerializeField] GameObject[] centerLinePointCollections; // Parent objects of points previously dropped behind the car

	bool trackErrors = false; // Track the errors off of the lane's center line
	GameObject centerLine; // The parent object of the center line point objects we are currently using
	Vector2[] centerLinePoints; // The list of points that mark the center line of the lane
	int currentLane = 0; // The lane that we are currently on
	int numLanes; // How many lanes there are
	float steeringAngle = 0f; // Raw input from the steering wheel or keyboard
	bool currentlyRecording; // Are we currently saving screenshots?
	Rigidbody rb; // Physics body of the car

	Vector3 initialPosition;
	Vector3 lastPosition;

	void Start () {
		if (drivingMode == DrivingMode.AutonomousVarianceTest) {
			rb = GetComponent<Rigidbody> ();
			SwitchLanes ();
			trackErrors = true;
			drivingMode = DrivingMode.Autonomous;
		}

		foreach (var wheel in GetComponentsInChildren<WheelCollider>()) {
			wheel.ConfigureVehicleSubsteps (5f, 12, 15);
		}

		if (drivingMode != DrivingMode.Manual) {
			StartCoroutine (RecordFrame ());
			if (drivingMode != DrivingMode.Recording) {
				StartCoroutine (HandleAutonomousSteering ());
			}
		}

		if (drawLine) {
			centerLine = new GameObject ("Center Line");
			centerLine.transform.position = Vector3.zero;
			InvokeRepeating ("DrawCenterLinePoint", 0.5f, 0.5f);
		}

		currentlyRecording = (drivingMode != DrivingMode.Recording) || !enableWheel;

		initialPosition = transform.position;

		numLanes = centerLinePointCollections.Length;
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

		foreach (var driveWheel in driveWheels) {
			driveWheel.steerAngle = currentSteeringAngleDegrees;
			driveWheel.motorTorque = torque;
		}

		var recordingButton = Input.GetAxisRaw ("EnableRecording");
		if (recordingButton > 0)
			currentlyRecording = true;
		else if (recordingButton < 0)
			currentlyRecording = false;

		if (trackErrors)
			Utility.CalculateCenterLineError (centerLinePoints, transform.position);

#if UNITY_EDITOR_WIN
		print (currentlyRecording);
#else
        print(steeringAngle);
#endif
	}

	IEnumerator RecordFrame () {
		var camera = GetComponentInChildren<Camera> ();
#if UNITY_EDITOR_WIN
        while (true) {
#else
        for (var i = 0; true; i++) {
#endif
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
			RenderTexture.active = null;
			Destroy(renderTexture);
			var bytes = screenShot.EncodeToPNG();
#if UNITY_EDITOR_WIN
			var unixTimestamp = (uint)(DateTime.UtcNow.Subtract (new DateTime (1970, 1, 1))).TotalMilliseconds;
			var steeringAngleText = currentSteeringAngleDegrees.ToString ("F7");
			var filename = "sim/" + unixTimestamp + "_" + steeringAngleText + ".png";
#else
			var filename = tmpPath + "temp.png";
#endif
			File.WriteAllBytes(filename, bytes);
#if !UNITY_EDITOR_WIN
			File.Move (tmpPath + "temp.png", tmpPath + "sim" + i + ".png");
#endif
			yield return new WaitForEndOfFrame ();
		}
	}

	IEnumerator HandleAutonomousSteering () {
		while (true) {
			var maxIndex = -1;
			foreach (var path in Directory.GetFiles(tmpPath)) {
				if (path.Contains ("sim.txt")) {
					var index = int.Parse(path.Substring (tmpPath.Length, path.Length - (7 + tmpPath.Length)));
					if (index > maxIndex)
						maxIndex = index;
				}
			}
			var streamReader = new StreamReader (tmpPath + maxIndex + "sim.txt");
			var fileContents = streamReader.ReadToEnd ();
			var inverseTurningRadius = float.Parse (fileContents);
			var rawSteeringAngle = Utility.InverseTurningRadiusToSteeringAngleDegrees (inverseTurningRadius);
			steeringAngle = useLowPassFilter ? Utility.LowPassFilter (rawSteeringAngle) : rawSteeringAngle;
			yield return null;
		}
	}

	void SwitchLanes () {
		if (currentLane > 0)
			Utility.SaveTestResults (currentLane);

		if (currentLane <= numLanes) {
			centerLine = centerLinePointCollections [currentLane];
			var centerLineTransforms = centerLine.GetComponentsInChildren<Transform> ();
			var centerLinePointObjects = centerLineTransforms.Skip (1).Distinct ().ToArray ();
			var startingPointTransform = centerLinePointObjects [0];
			transform.position = startingPointTransform.position;
			transform.rotation = startingPointTransform.rotation;
			rb.velocity = Vector3.zero;
			centerLinePoints = new Vector2[centerLinePointObjects.Length];
	
			for (var i = 0; i < centerLinePoints.Length; i++) {
				var position = centerLinePointObjects [i].position;
				centerLinePoints [i] = Utility.ProjectOntoXZPlane (position);
			}
				
			currentLane++;
			Invoke ("SwitchLanes", timeSpentOnLane);
		}
	}

	void DrawCenterLinePoint () {
		var point = new GameObject ("Point");
		point.transform.position = transform.position;
		point.transform.SetParent (centerLine.transform);
	}

	float currentSteeringAngleDegrees {
		get {
			var steeringAngleDegrees = 90f * steeringAngle * steeringAngleMultiplier;
			return steeringAngleDegrees;
		}
	}

	enum DrivingMode {
		Recording,
		Autonomous,
		AutonomousVarianceTest,
		Manual
	}
}
