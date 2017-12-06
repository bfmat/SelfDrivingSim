﻿using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Collections;

// The main car class that handles movement and recording
sealed class Car : MonoBehaviour
{
    // Scaling factor for the steering wheel input 
    const float steeringAngleMultiplier = 0.1f;
    // The amount to increase the steering angle per key press (for keyboard controls)
    const float minSteeringBump = 0.005f;
    // Torque to constantly apply to the front wheels
    const float torque = 16f;
    // During automated tests, spend this many seconds on each individual lane
    const float timeSpentOnLane = 100f;
    // Width and height of saved screenshots
    const int resWidth = 320, resHeight = 180;
    // Path to save images in during autonomous driving
    const string tmpPath = "/tmp/";
    // Apply the low pass filter in Utility to steering angles in autonomous mode
    const bool useLowPassFilter = true;

    // Manual, recording, autonomous, automated test
    [SerializeField] DrivingMode drivingMode;
    // The two front wheels
    [SerializeField] WheelCollider[] driveWheels;
    // Use a steering wheel to manually drive the car (I have only tested a Logitech G29)
    [SerializeField] bool enableWheel;
    // Drop points behind the car (used for automated testing)
    [SerializeField] bool drawLine;
    // Parent objects of points previously dropped behind the car
    [SerializeField] GameObject[] centerLinePointCollections;

    // Track the errors off of the lane's center line
    bool trackErrors = false;
    // The parent object of the center line point objects we are currently using
    GameObject centerLine;
    // The list of points that mark the center line of the lane
    Vector2[] centerLinePoints;
    // The lane that we are currently on
    int currentLane = 0;
    // How many lanes there are
    int numLanes;
    // Raw input from the steering wheel or keyboard
    float steeringAngle = 0f;
    // Are we currently saving screenshots?
    bool currentlyRecording;
    // Physics body of the car
    Rigidbody rb;

    // Main initialization function
    void Start()
    {
        // If we are testing the car's standard deviation from the center line
        if (drivingMode == DrivingMode.AutonomousVarianceTest)
        {
            // Set the car's position to the first lane
            SwitchLanes();
            // Set the flag so that errors will be tracked and recorded
            trackErrors = true;
            // Do everything else in regular autonomous mode
            drivingMode = DrivingMode.Autonomous;
        }

        // For each of the car's wheel colliders
        foreach (var wheel in GetComponentsInChildren<WheelCollider>())
        {
            // Configure the wheel's physical properties
            wheel.ConfigureVehicleSubsteps(5f, 12, 15);
        }

        // If the car is in recording or autonomous mode
        if (drivingMode != DrivingMode.Manual)
        {
            // Start recording images
            StartCoroutine(RecordFrame());
            // If we are in autonomous mode
            if (drivingMode == DrivingMode.Autonomous)
            {
                // Start automatic control of the steering angle
                StartCoroutine(HandleAutonomousSteering());
            }
        }

        // If we are currently dropping points behind the car
        if (drawLine)
        {
            // Create an empty center line object that will serve as the parent of all the center line points
            centerLine = new GameObject("Center Line");
            centerLine.transform.position = Vector3.zero;
            // Start running the function that drops points on the center line twice per second
            InvokeRepeating("DrawCenterLinePoint", 0.5f, 0.5f);
        }

        // Get the robot's rigidbody and store it in a global variable
        rb = GetComponent<Rigidbody>();

        // Set the global variable containing the number of lanes
        numLanes = centerLinePointCollections.Length;

        // If either the wheel is disabled, or we are not in recording mode, enable recording right away (this is so that we do not have to press a button to go into autonomous driving)
        currentlyRecording = (drivingMode != DrivingMode.Recording) || !enableWheel;
    }

    // Update function, called 50 times per second
    void FixedUpdate()
    {
        // If we are in recording or manual mode
        if (drivingMode != DrivingMode.Autonomous)
        {
            // If the steering wheel is enabled
            if (enableWheel)
            {
                // Set the steering angle using the steering wheel's input
                steeringAngle = Input.GetAxis("Steering");
            }
            // Otherwise if the steering is disabled
            else
            {
                // If the A key is pressed, move the steering angle to the left
                if (Input.GetKey(KeyCode.A))
                    steeringAngle -= minSteeringBump;
                // If the D key is pressed, move the steering angle to the right
                if (Input.GetKey(KeyCode.D))
                    steeringAngle += minSteeringBump;
            }
        }

        foreach (var driveWheel in driveWheels)
        {
            driveWheel.steerAngle = scaledSteeringAngleDegrees;
            driveWheel.motorTorque = torque;
        }

        var recordingButton = Input.GetAxisRaw("EnableRecording");
        if (recordingButton > 0)
            currentlyRecording = true;
        else if (recordingButton < 0)
            currentlyRecording = false;

        if (trackErrors)
            Utility.CalculateCenterLineError(centerLinePoints, transform.position);
    }

    // Coroutine to capture and save screenshots for either recording or testing
    IEnumerator RecordFrame()
    {
        // Get the main camera attached to the car
        var mainCamera = GetComponentInChildren<Camera>();

        // Loop forever, incrementing a counter used in the image file name
        for (var i = 0; ; i++)
        {
            // Loop forever until recording has started
            do
            {
                // Continue looping after the end of the current frame
                yield return new WaitForEndOfFrame();
            } while (!currentlyRecording);

            // Create a texture and render the camera to it
            var renderTexture = new RenderTexture(resWidth, resHeight, 24);
            mainCamera.targetTexture = renderTexture;
            mainCamera.Render();
            RenderTexture.active = renderTexture;
            // Convert the rendered image into a Texture2D
            var screenShot = new Texture2D(resWidth, resHeight, TextureFormat.RGB24, false);
            screenShot.ReadPixels(new Rect(0, 0, resWidth, resHeight), 0, 0);
            // Set the rendering target back to the screen
            mainCamera.targetTexture = null;
            RenderTexture.active = null;
            // Destroy the texture to prevent a memory leak
            Destroy(renderTexture);
            // Encode the texture into a PNG image
            var encodedImage = screenShot.EncodeToPNG();

            // File to save the screenshot in
            string fileName;
            // If we are in recording mode, name the file using the current timestamp
            if (drivingMode == DrivingMode.Recording)
            {
                // Get the present Unix timestamp in milliseconds
                var unixTimestamp = (uint)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalMilliseconds;
                // Convert it to a string with 7 digits of precision
                var steeringAngleText = scaledSteeringAngleDegrees.ToString("F7");
                // Save the file in the `sim` subfolder of the project folder
                fileName = "sim/" + unixTimestamp + "_" + steeringAngleText + ".png";
            }
            // Otherwise, save the file in the temp folder
            else
            {
                fileName = tmpPath + "temp.png";
            }
            // Write the contents of the image to a file
            File.WriteAllBytes(fileName, encodedImage);
            // If we are not recording, move the temp file to a permanent numbered filename
            // This is done so that the files are never read by other programs when partially written
            if (drivingMode != DrivingMode.Recording)
            {
                File.Move(tmpPath + "temp.png", tmpPath + "sim" + i + ".png");
            }

            // Wait until the next frame to save another image
            yield return new WaitForEndOfFrame();
        }
    }

    IEnumerator HandleAutonomousSteering()
    {
        while (true)
        {
            var maxIndex = -1;
            foreach (var path in Directory.GetFiles(tmpPath))
            {
                if (path.Contains("sim.txt"))
                {
                    var index = int.Parse(path.Substring(tmpPath.Length, path.Length - (7 + tmpPath.Length)));
                    if (index > maxIndex)
                        maxIndex = index;
                }
            }
            var streamReader = new StreamReader(tmpPath + maxIndex + "sim.txt");
            var fileContents = streamReader.ReadToEnd();
            var rawSteeringAngle = float.Parse(fileContents);
            steeringAngle = useLowPassFilter ? Utility.LowPassFilter(rawSteeringAngle) : rawSteeringAngle;
            yield return null;
        }
    }

    void SwitchLanes()
    {
        if (currentLane > 0)
            Utility.SaveTestResults(currentLane);

        if (currentLane <= numLanes)
        {
            centerLine = centerLinePointCollections[currentLane];
            var centerLineTransforms = centerLine.GetComponentsInChildren<Transform>();
            var centerLinePointObjects = centerLineTransforms.Skip(1).Distinct().ToArray();
            var startingPointTransform = centerLinePointObjects[0];
            transform.position = startingPointTransform.position;
            transform.rotation = startingPointTransform.rotation;
            rb.velocity = Vector3.zero;
            centerLinePoints = new Vector2[centerLinePointObjects.Length];

            for (var i = 0; i < centerLinePoints.Length; i++)
            {
                var position = centerLinePointObjects[i].position;
                centerLinePoints[i] = Utility.ProjectOntoXZPlane(position);
            }

            currentLane++;
            Invoke("SwitchLanes", timeSpentOnLane);
        }
    }

    // A function to create a child point of the center line at the robot's current position
    void DrawCenterLinePoint()
    {
        // Create the empty point object
        var point = new GameObject("Point");
        // Set its position to the robot's current position
        point.transform.position = transform.position;
        // Set the parent of the point to the global center line game object
        point.transform.SetParent(centerLine.transform);
    }

    // Get the current steering angle in degrees, multiplied by the coefficient that scales the angle of the wheels
    float scaledSteeringAngleDegrees
    {
        // This is a read-only property, so only a getter is provided
        get
        {
            // Scale it so that -1 represents 90 degrees to the right and 1 represents 90 degrees to the right, and scale that by the constant multiplier
            return 90f * steeringAngle * steeringAngleMultiplier;
        }
    }

    // An enum containing the possible states of the car
    enum DrivingMode
    {
        // Driven manually without recording
        Manual,
        // Manual mode while saving images for training purposes
        Recording,
        // Controlled autonomously without analysis
        Autonomous,
        // Autonomous mode plus saving information related to the car's distance from the center of the road
        AutonomousVarianceTest
    }
}
