using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

// A collection of utility functions called at various times in the car class
static class Utility
{
    // Used to calculate the error off of a 2D center line of a 3D point projected into the same two dimensions as the line
    internal static float CalculateCenterLineError(Vector2[] centerLinePoints, Vector3 position)
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
        // Using the projected point, calculate the squared distance of the car from the nearest point on the center line and return it
        return (carPosition - projection).sqrMagnitude;
    }

    // Save the results of an autonomous testing run to a file
    internal static void SaveTestResults(List<float> squaredErrors, int fileNumber)
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

    // Project a three-dimensional point onto a two-dimensional plane by removing the Y value (vertical axis)
    internal static Vector2 ProjectOntoXZPlane(Vector3 _3DPoint)
    {
        // Create and return a 2D vector using the X and Z values of the 3D point for X and Y, respectively
        var _2DPoint = new Vector2(_3DPoint.x, _3DPoint.z);
        return _2DPoint;
    }
}
