using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

// A collection of utility functions called at various times in the car class
static class Utility
{
    // The weights of recent past steering angles in the low pass filters
    readonly static float[] lowPassParameters = { 1.0f, 0.8f, 0.3f, 0.1f };
    // The sum of all of the low pass filter parameters, used in the low pass filter calculations
    readonly static float lowPassParametersSum;
    // Past steering angles output by network, used for low pass filter
    static float[] previousSteeringAngles;
    // List of past squared errors during automated testing
    static List<float> squaredErrors = new List<float>();

    // Static initializer that is called when the class is created
    static Utility()
    {
        // Initialize the previous steering angles so its length is the same as the number of low pass parameters
        previousSteeringAngles = new float[lowPassParameters.Length];

        // Calculate the sum of the low pass filter parameters and store them in the global constant
        lowPassParametersSum = lowPassParameters.Sum();
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
        // Add it to the list of squared errors
        squaredErrors.Add(variance);
    }

    // Save the results of an autonomous testing run to a file
    internal static void SaveTestResults(int fileNumber)
    {
        // Add up all of the past squared errors from the center line
        var totalVariance = squaredErrors.Sum();
        // Calculate the square root of the average squared error from the center line, which is equivalent to the standard deviation of the car from the center line of the road
        var variance = totalVariance / squaredErrors.Count;
        var standardDeviation = Mathf.Sqrt(variance);
        // Convert the squared errors to strings with 7 decimal digits of precision
        var stringErrors = Array.ConvertAll(squaredErrors.ToArray(), x => x.ToString("F7"));
        // Add the standard deviation to the end of the list of strings
        var outputArray = stringErrors.Concat(new[] { "Standard deviation: " + standardDeviation }).ToArray();
        // Write the entire output array to a file
        File.WriteAllLines("sim/results" + fileNumber + ".txt", outputArray);
        // Clear the list of squared errors
        squaredErrors = new List<float>();
    }

    // A simple low pass filter that removes high-frequency noise from the autonomous driving system's output so that the car drives more smoothly
    internal static float LowPassFilter(float rawSteeringAngle)
    {
        // Iterate over all of the previous steering angles except for the last one, and shift them down to the end of the array
        for (var i = 0; i < previousSteeringAngles.Length - 1; i++)
        {
            previousSteeringAngles[i + 1] = previousSteeringAngles[i];
        }
        // Now that the first slot in the steering array has been freed up, set it to the current steering angle
        previousSteeringAngles[0] = rawSteeringAngle;

        // An accumulator for weighted angles
        var weightedAngleSum = 0f;
        // Iterate over the array of steering angles, which should have the same length as the array of low pass filter parameters
        for (var i = 0; i < previousSteeringAngles.Length; i++)
        {
            // Multiply the steering angle by the corresponding parameter to get a weighted angle
            var weightedAngle = previousSteeringAngles[i] * lowPassParameters[i];
            // Add it to the accumulator
            weightedAngleSum += weightedAngle;
        }
        // Divide the accumulator by the sum of the low pass parameters to get what is essentially a weighted average of the array of the most recent steering angles including the angle just added
        var nextSteeringAngle = weightedAngleSum / lowPassParametersSum;
        // Return this weighted average, which is used as the next steering angle
        return nextSteeringAngle;
    }

    // Project a three-dimensional point onto a two-dimensional plane by removing the Y value (vertical axis)
    internal static Vector2 ProjectOntoXZPlane(Vector3 _3DPoint)
    {
        // Create and return a 2D vector using the X and Z values of the 3D point for X and Y, respectively
        var _2DPoint = new Vector2(_3DPoint.x, _3DPoint.z);
        return _2DPoint;
    }
}
