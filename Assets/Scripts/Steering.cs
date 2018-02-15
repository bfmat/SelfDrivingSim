using UnityEngine;

// A class that handles steering functionality including translation between steering wheel angles and car wheel angles and backlash
static class Steering
{
    // The width in steering angle units of the dead band
    const float deadBandWidth = 0.04f;
    // The center of the current backlash dead band in steering angle units
    static float deadBandCenter = 0f;

    // The amount in steering angle units that the wheel should move towards the edge of the dead band per second
    const float changePerSecond = 0.01f;
    // The time that the sign of the input steering angle has changed
    static float lastSteeringAngleSignChange;
    // The last sign of the steering angle (initialize it to 0 so that it will immediately be considered a change)
    static float lastSign = 0f;

    // The main function that takes a steering wheel from the steering system and returns a car wheel angle in degrees
    internal static float getWheelAngle(float steeringAngle, bool useBacklash)
    {
        // Make a copy of the steering angle that will be modified if required
        var processedSteeringAngle = steeringAngle;
        // If backlash is enabled
        if (useBacklash)
        {
            // Apply backlash to the steering angle
            processedSteeringAngle = introduceBacklash(steeringAngle);
            // Shift the steering angle to the edge of the dead band depending on how long it has been on this side of the road
            processedSteeringAngle = moveToEdgeOfDeadBand(steeringAngle, processedSteeringAngle);
        }
        // Multiply the processed steering angle by a constant to convert it to the approximate length of the opposite side of an angle where the wheel is in line with the hypotenuse and the adjacent is 1 unit
        var triangleOppositeLength = processedSteeringAngle * 1.038f;
        // Calculate the inverse tangent of the length of the opposite, which will equal the angle of the wheel in radians because the length of the adjacent is 1
        var wheelAngleRadians = Mathf.Atan(triangleOppositeLength);
        // Convert the wheel angle to degrees and return it
        return wheelAngleRadians * Mathf.Rad2Deg;
    }

    // A function to process a steering angle before conversion and introduce backlash so that steering does not respond immediately when reversing direction
    static float introduceBacklash(float steeringAngle)
    {
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

    // Based on the sign of the input steering angle, move the output steering angle towards the corresponding edge of the dead band; this simulates the property of a car to head towards the ditch when it passes the crown of the road in the center
    static float moveToEdgeOfDeadBand(float originalSteeringAngle, float steeringAngleWithBacklashApplied)
    {
        // Get the sign of the original steering angle
        var sign = Mathf.Sign(originalSteeringAngle);
        // If the sign just changed, update the relevant variables and return the steering angle with backlash without further modification
        if (sign == lastSign)
        {
            lastSign = sign;
            lastSteeringAngleSignChange = Time.timeSinceLevelLoad;
            return steeringAngleWithBacklashApplied;
        }
        // Otherwise, add to the steering angle with backlash applied
        else
        {
            // Multiply the delta since the last sign change by a scaling factor, and also by the negative of the new sign, which will send the car towards the side of the road that it is currently closest to
            var delta = Time.timeSinceLevelLoad - lastSteeringAngleSignChange;
            var offset = delta * changePerSecond * -sign;
            // Add the delta to the steering angle, bounding at the edge of the dead band
            var offsetSteeringAngle = steeringAngleWithBacklashApplied + offset;
            var deadBandRadius = deadBandWidth / 2;
            var upperLimit = deadBandCenter + deadBandRadius;
            var lowerLimit = deadBandCenter - deadBandRadius;
            if (offsetSteeringAngle > upperLimit)
            {
                offsetSteeringAngle = upperLimit;
            }
            if (offsetSteeringAngle < lowerLimit)
            {
                offsetSteeringAngle = lowerLimit;
            }
            return offsetSteeringAngle;
        }
    }
}
