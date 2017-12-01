using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

// A collection of utility functions called at various times in the car class
static class Utility
{

    const float wheelBase = 0.914f; // The distance from the center of the front wheels to the center of the back wheels
    readonly static float[] lowPassParameters = { 1.0f, 0.8f, 0.3f, 0.1f }; // The weights of recent past steering angles in the low pass filters
    static float[] previousSteeringAngles; // Past steering angles output by network, used for low pass filter
    static List<float> errors = new List<float>(); // List of past errors during automated testing

    // Static initializer that is called when the class is created
    static Utility()
    {
        // Initialize the previous steering angles so its length is the same as the number of low pass parameters
        previousSteeringAngles = new float[lowPassParameters.Length];
    }

    // Used to calculate the error off of a 2D center line of a 3D point projected into the same two dimensions as the line
    internal static void CalculateCenterLineError(Vector2[] centerLinePoints, Vector3 position)
    {
        // Project the position of the car onto a 2D plane, essentially removing the vertical position
        var carPosition = ProjectOntoXZPlane(position);
        // For each of the points along the center of the road, calculate the squared distance from that point to the position of the car in two dimensions
        var centerLineDistancesFromCar = from point in centerLinePoints select (carPosition - point).sqrMagnitude;
        // Find the two smallest distances in the list, and use those to get the two points closest to the car by getting the index of the distance and getting the same index in the list of center line points
        var closestPoints = (
            from distance in centerLineDistancesFromCar
            orderby distance ascending
            select centerLinePoints[Array.IndexOf(centerLineDistancesFromCar.ToArray(), distance)]
        ).Take(2).ToArray();
        // Get the first and second closest points out of the list
        var firstPoint = closestPoints[0];
        var secondPoint = closestPoints[1];

        // Calculate the slope and the Y-intercept of the line between the two points
        var riseRun = secondPoint - firstPoint;
        var slope = riseRun.y / riseRun.x;
        var intercept = firstPoint.y - (firstPoint.x * slope);
        // Calculate the slope and Y-intercept of the perpendicular line that passes through the car's position
        var perpendicularSlope = -1 / slope;
        var distanceLineIntercept = carPosition.y - (carPosition.x * perpendicularSlope);
        // Use the perpendicular line to project the position of the car onto the line between the two points on the center line closest to the car
        var xProjection = (distanceLineIntercept - intercept) / (slope - perpendicularSlope);
        var projection = new Vector2(xProjection, (slope * xProjection) + intercept);
        // Using the projected point, calculate the squared distance of the car from the nearest point on the center line
        var variance = (carPosition - projection).sqrMagnitude;
        // Add it to the list of variances
        errors.Add(variance);
    }

    internal static void SaveTestResults(int fileNumber)
    {
        var totalVariance = 0f;
        foreach (var error in errors)
        {
            totalVariance += error;
        }

        var meanVariance = totalVariance / errors.Count;
        var standardDeviation = Mathf.Sqrt(meanVariance);
        var stringErrors = Array.ConvertAll(errors.ToArray(), x => x.ToString("F7"));
        var outputArray = stringErrors.Concat(new[] { "Standard deviation: " + standardDeviation }).ToArray();
        File.WriteAllLines("sim/results" + fileNumber + ".txt", outputArray);

        errors = new List<float>();
    }

    internal static float LowPassFilter(float rawSteeringAngle)
    {
        for (var i = 0; i < previousSteeringAngles.Length - 1; i++)
        {
            previousSteeringAngles[i + 1] = previousSteeringAngles[i];
        }
        previousSteeringAngles[0] = rawSteeringAngle;

        var weightedAngleSum = 0f;
        var parameterSum = 0f;
        for (var i = 0; i < previousSteeringAngles.Length; i++)
        {
            parameterSum += lowPassParameters[i];
            var weightedAngle = previousSteeringAngles[i] * lowPassParameters[i];
            weightedAngleSum += weightedAngle;
        }

        return weightedAngleSum / parameterSum;
    }

    internal static Vector2 ProjectOntoXZPlane(Vector3 _3DPoint)
    {
        var _2DPoint = new Vector2(_3DPoint.x, _3DPoint.z);
        return _2DPoint;
    }
}
