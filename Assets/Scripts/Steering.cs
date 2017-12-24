using UnityEngine;

// A class that handles steering functionality including translation between steering wheel angles and car wheel angles and backlash
static class Steering
{
    // The width in steering angle units of the dead band
    const float deadBandWidth = 0.05f;

    // The center of the current backlash dead band in steering angle units
    static float deadBandCenter = 0f;

    // The main function that takes a steering wheel from the steering system and returns a car wheel angle in degrees
    internal static float getWheelAngle(float steeringAngle)
    {
        // Introduce backlash to the steering angle
        var steeringAngleWithBacklash = introduceBacklash(steeringAngle);
        // Multiply the processed steering angle by a constant to convert it to the approximate length of the opposite side of an angle where the wheel is in line with the hypotenuse and the adjacent is 1 unit
        var triangleOppositeLength = steeringAngle * 1.038f;
        // Calculate the inverse tangent of the length of the opposite, which will equal the angle of the wheel in radians because the length of the adjacent is 1
        var wheelAngleRadians = Mathf.Atan(triangleOppositeLength);
        // Convert the wheel angle to degrees and return it
        return wheelAngleRadians * Mathf.Rad2Deg;
    }

    // A function to process a steering angle before conversion and introduce backlash so that steering does not respond immediately when reversing direction
    static float introduceBacklash(float steeringAngle) {
        // If the absolute difference between the current steering angle and the center of the dead band is greater than half of the width of the dead band
        if (Mathf.Abs(steeringAngle - deadBandCenter) > deadBandWidth / 2)
        {
            // Calculate an offset that is either above or below the current steering angle depending on whether it is greater than or less than the dead band center
            var deadBandOffset = steeringAngle > deadBandCenter ? -deadBandWidth : deadBandWidth;
            // Add the offset to the current steering angle to get the new dead band center, which should leave the current steering angle at the edge of the new dead band
            deadBandCenter = steeringAngle + deadBandOffset;
        }
        // Return the center of the dead band as the steering angle, which will be in the same location as last update if it has not changed during this function call
        return deadBandCenter;
    }
}