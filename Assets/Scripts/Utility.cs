using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

static class Utility {

	const float wheelBase = 0.914f; // The distance from the center of the front wheels to the center of the back wheels
	readonly static float[] lowPassParameters = { 1.0f, 0.3f, 0.2f, 0.1f }; // The weights of recent past steering angles in the low pass filters
	static float[] previousSteeringAngles; // Past steering angles output by network, used for low pass filter
	static List<float> errors = new List<float> (); // List of past errors during automated testing

	static Utility() {
		previousSteeringAngles = new float[lowPassParameters.Length];
	}

	internal static void CalculateCenterLineError (Vector2[] centerLinePoints, Vector3 position) {
		var firstPoint = centerLinePoints [0];
		var secondPoint = centerLinePoints [1];
		var carPosition = ProjectOntoXZPlane (position);
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
		errors.Add (variance);
	}

	internal static void SaveTestResults (int fileNumber) {
		var totalVariance = 0f;
		foreach (var error in errors) {
			totalVariance += error;
		}

		var meanVariance = totalVariance / errors.Count;
		var standardDeviation = Mathf.Sqrt (meanVariance);
		var stringErrors = Array.ConvertAll (errors.ToArray (), x => x.ToString ("F7"));
		var outputArray = stringErrors.Concat (new [] { "Standard deviation: " + standardDeviation }).ToArray ();
		File.WriteAllLines ("sim/results" + fileNumber + ".txt", outputArray);

		errors = new List<float> ();
	}

	internal static float LowPassFilter (float rawSteeringAngle) {
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

	internal static float InverseTurningRadiusToSteeringAngleDegrees (float inverseTurningRadius) {
		var wheelAngleRadians = Mathf.Atan (inverseTurningRadius * wheelBase);
		var wheelAngleDegrees = wheelAngleRadians * Mathf.Rad2Deg;
		return wheelAngleDegrees;
	}

	internal static float SteeringAngleDegreesToInverseTurningRadius (float wheelAngleDegrees) {
		var wheelAngleRadians = wheelAngleDegrees * Mathf.Deg2Rad;
		var inverseTurningRadius = Mathf.Tan (wheelAngleRadians) / wheelBase;
		return inverseTurningRadius;
	}

	internal static Vector2 ProjectOntoXZPlane (Vector3 _3DPoint) {
		var _2DPoint = new Vector2(_3DPoint.x, _3DPoint.z);
		return _2DPoint;
	}
}
