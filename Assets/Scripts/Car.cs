using UnityEngine;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;

public class Car : MonoBehaviour {
	
	const float steeringAngleMultiplier = 0.1f;
	const float minSteeringBump = 0.005f;
	const float torque = 16f;
	const int resWidth = 200, resHeight = 150;
	const string tmpPath = "/run/user/1000/gvfs/sftp:host=192.168.2.52,user=nvidia/home/nvidia/tmp/";
	const string centerLineTag = "CenterLine";

	[SerializeField] DrivingMode drivingMode;
	[SerializeField] WheelCollider[] driveWheels;
	[SerializeField] bool enableWheel;
	[SerializeField] bool drawLine;

	bool trackErrors = false;
	GameObject centerLine;
	Vector2[] centerLinePoints;
	float steeringAngle = 0f;
	List<string> labels = new List<string> ();
	List<float> errors = new List<float> ();
	bool currentlyRecording;

	void Start () {
		if (drivingMode == DrivingMode.AutonomousVarianceTest) {
			Invoke ("PrintTestResults", 10f);
			trackErrors = true;
			drivingMode = DrivingMode.Autonomous;
		}

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

		if (drawLine) {
			centerLine = new GameObject ("Center Line");
			centerLine.tag = centerLineTag;
			centerLine.transform.position = Vector3.zero;
			InvokeRepeating ("DrawCenterLinePoint", 0.5f, 0.5f);
		}
	
		if (trackErrors) {
			centerLine = GameObject.FindGameObjectWithTag (centerLineTag);
			var centerLinePointObjects = centerLine.GetComponentsInChildren<Transform> ();
			centerLinePoints = new Vector2[centerLinePointObjects.Length];
			for (var i = 0; i < centerLinePoints.Length; i++) {
				var position = centerLinePointObjects [i].position;
				centerLinePoints [i] = ProjectOntoXZPlane (position);
			}
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

		if (trackErrors) {
			var error = CalculateCenterLineError ();
			errors.Add (error);
		}
		
#if UNITY_EDITOR_WIN
		//print (currentlyRecording);
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
			var filename = tmpPath + "temp.png";
#endif
			File.WriteAllBytes(filename, bytes);
#if UNITY_EDITOR_LINUX
			File.Move (tmpPath + "temp.png", tmpPath + "sim" + i + ".png");
#endif
			labels.Add (filename + "," + steeringAngle.ToString ("F7"));
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
			steeringAngle = float.Parse (fileContents);
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

	void PrintTestResults () {
		var totalVariance = 0f;
		foreach (var error in errors) {
			totalVariance += error;
		}
		var meanVariance = totalVariance / errors.Count;
		var standardDeviation = Mathf.Sqrt (meanVariance);
		print ("Standard deviation from center line was " + standardDeviation);
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

	void WriteLabels () {
		var labelsArray = labels.ToArray ();
		File.WriteAllLines ("sim/labels.csv", labelsArray);
	}

	enum DrivingMode {
		Recording,
		Autonomous,
		AutonomousVarianceTest,
		Manual
	}
}
