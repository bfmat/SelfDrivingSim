using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

public class Car : MonoBehaviour {
	
	const float steeringAngleMultiplier = 0.1f; // Scaling factor for the steering wheel input 
	const float minSteeringBump = 0.005f; // The amount to increase the steering angle per key press (for keyboard controls)
	const float torque = 16f; // Torque to constantly apply to the front wheels
	const float timeSpentOnLane = 100f; // During automated tests, spend this many seconds on each individual lane
	const float wheelBase = 0.914f; // The distance from the center of the front wheels to the center of the back wheels
	const int resWidth = 200, resHeight = 150; // Width and height of saved screenshots
	const string tmpPath = "/tmp/"; // Path to save images in during autonomous driving
	const bool useLowPassFilter = true;

	readonly float[] lowPassParameters = { 1.0f, 0.3f, 0.2f, 0.1f }; // The weights of recent past steering angles in the low pass filters

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
	List<float> errors; // List of past errors during automated testing
	bool currentlyRecording; // Are we currently saving screenshots?
	Rigidbody rb; // Physics body of the car
	float[] previousSteeringAngles; // Past steering angles output by network, used for low pass filter

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
				previousSteeringAngles = new float[lowPassParameters.Length];
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
			driveWheel.steerAngle = GetCurrentSteeringAngleDegrees(steeringAngle);
			driveWheel.motorTorque = torque;
		}

		var recordingButton = Input.GetAxisRaw ("EnableRecording");
		if (recordingButton > 0)
			currentlyRecording = true;
		else if (recordingButton < 0)
			currentlyRecording = false;

		if (trackErrors) {
			var error = CalculateCenterLineError ();
			errors.Add (error);
		}

#if UNITY_EDITOR_WIN
		print (currentlyRecording);
#else
		print (Time.timeSinceLevelLoad);
#endif
	}

	IEnumerator RecordFrame () {
		var camera = GetComponentInChildren<Camera> ();
#if UNITY_EDITOR_LINUX
		for (var i = 0; true; i++) {
#else
		while (true) {
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
			var steeringAngleText = GetCurrentSteeringAngleDegrees(steeringAngle).ToString ("F7");
			var filename = "sim/" + unixTimestamp + "_" + steeringAngleText + ".png";
#else
			var filename = tmpPath + "temp.png";
#endif
			File.WriteAllBytes(filename, bytes);
#if UNITY_EDITOR_LINUX
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
			var rawSteeringAngle = InverseTurningRadiusToSteeringAngleDegrees (inverseTurningRadius);
			steeringAngle = useLowPassFilter ? LowPassFilter (rawSteeringAngle) : rawSteeringAngle;
			yield return null;
		}
	}

	float CalculateCenterLineError () {
		var firstPoint = centerLinePoints [0];
		var secondPoint = centerLinePoints [1];
		var carPosition = ProjectOntoXZPlane (transform.position);
		var firstDelta = (carPosition - firstPoint).sqrMagnitude;
		var secondDelta = (carPosition - secondPoint).sqrMagnitude;

		foreach (var point in centerLinePoints) {
			if (point == firstPoint || point == secondPoint)
				continue;
			var pointDelta = (carPosition - point).sqrMagnitude;
			if (pointDelta < firstDelta) {
				secondPoint = firstPoint;
				secondDelta = firstDelta;
				firstPoint = point;
				firstDelta = pointDelta;
			} else if (pointDelta < secondDelta) {
				secondPoint	= point;
				secondDelta = pointDelta;
			}
		}

		var riseRun = secondPoint - firstPoint;
		var slope = riseRun.y / riseRun.x;
		var intercept = firstPoint.y - (firstPoint.x * slope);
		var perpendicularSlope = -1 / slope;
		var distanceLineIntercept = carPosition.y - (carPosition.x * perpendicularSlope);

		var xProjection = (distanceLineIntercept - intercept) / (slope - perpendicularSlope);
		var projection = new Vector2 (xProjection, (slope * xProjection) + intercept);
		var variance = (carPosition - projection).sqrMagnitude;
		return variance;
	}

	void SwitchLanes () {
		if (currentLane > 0)
			SaveTestResults ();

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
				centerLinePoints [i] = ProjectOntoXZPlane (position);
			}
				
			errors = new List<float> ();
			currentLane++;
			Invoke ("SwitchLanes", timeSpentOnLane);
		}
	}

	void SaveTestResults () {
		var totalVariance = 0f;
		foreach (var error in errors) {
			totalVariance += error;
		}

		var meanVariance = totalVariance / errors.Count;
		var standardDeviation = Mathf.Sqrt (meanVariance);
		print ("Standard deviation from center line was " + standardDeviation);
		var stringErrors = Array.ConvertAll (errors.ToArray (), x => x.ToString ("F7"));
		var outputArray = stringErrors.Concat (new [] { "Standard deviation: " + standardDeviation }).ToArray ();
		File.WriteAllLines ("sim/results" + currentLane + ".txt", outputArray);
	}

	float LowPassFilter (float rawSteeringAngle) {
		for (var i = 0; i < previousSteeringAngles.Length - 1; i++) {
			previousSteeringAngles [i + 1] = previousSteeringAngles [i];
		}
		previousSteeringAngles [0] = rawSteeringAngle;

		var weightedAngleSum = 0f;
		var parameterSum = 0f;
		for (var i = 0; i < previousSteeringAngles.Length; i++) {
			parameterSum += lowPassParameters [i];
			var weightedAngle = previousSteeringAngles [i] * lowPassParameters [i];
			weightedAngleSum += weightedAngle;
		}

		return weightedAngleSum / parameterSum;
	}

	float InverseTurningRadiusToSteeringAngleDegrees (float inverseTurningRadius) {
		var wheelAngleRadians = Mathf.Atan (inverseTurningRadius * wheelBase);
		var wheelAngleDegrees = wheelAngleRadians * Mathf.Rad2Deg;
		return wheelAngleDegrees;
	}

	float SteeringAngleDegreesToInverseTurningRadius (float wheelAngleDegrees) {
		var wheelAngleRadians = wheelAngleDegrees * Mathf.Deg2Rad;
		var inverseTurningRadius = Mathf.Tan (wheelAngleRadians) / wheelBase;
		return inverseTurningRadius;
	}

	void DrawCenterLinePoint () {
		var point = new GameObject ("Point");
		point.transform.position = transform.position;
		point.transform.SetParent (centerLine.transform);
	}

	Vector2 ProjectOntoXZPlane (Vector3 _3DPoint) {
		var _2DPoint = new Vector2(_3DPoint.x, _3DPoint.z);
		return _2DPoint;
	}

	float GetCurrentSteeringAngleDegrees (float steeringAngle) {
		var steeringAngleDegrees = 90f * steeringAngle * steeringAngleMultiplier;
		return steeringAngleDegrees;
	}

	enum DrivingMode {
		Recording,
		Autonomous,
		AutonomousVarianceTest,
		Manual
	}
}
