using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Collections;

// The main car class that handles movement and recording
sealed class Car : MonoBehaviour
{

    const float steeringAngleMultiplier = 0.1f; // Scaling factor for the steering wheel input 
    const float minSteeringBump = 0.005f; // The amount to increase the steering angle per key press (for keyboard controls)
    const float torque = 16f; // Torque to constantly apply to the front wheels
    const float timeSpentOnLane = 100f; // During automated tests, spend this many seconds on each individual lane
    const int resWidth = 320, resHeight = 180; // Width and height of saved screenshots
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

    void Start()
    {
        if (drivingMode == DrivingMode.AutonomousVarianceTest)
        {
            rb = GetComponent<Rigidbody>();
            SwitchLanes();
            trackErrors = true;
            drivingMode = DrivingMode.Autonomous;
        }

        foreach (var wheel in GetComponentsInChildren<WheelCollider>())
        {
            wheel.ConfigureVehicleSubsteps(5f, 12, 15);
        }

        if (drivingMode != DrivingMode.Manual)
        {
            StartCoroutine(RecordFrame());
            if (drivingMode != DrivingMode.Recording)
            {
                StartCoroutine(HandleAutonomousSteering());
            }
        }

        if (drawLine)
        {
            centerLine = new GameObject("Center Line");
            centerLine.transform.position = Vector3.zero;
            InvokeRepeating("DrawCenterLinePoint", 0.5f, 0.5f);
        }

        currentlyRecording = (drivingMode != DrivingMode.Recording) || !enableWheel;

        numLanes = centerLinePointCollections.Length;
    }

    void FixedUpdate()
    {
        if (drivingMode != DrivingMode.Autonomous)
        {
            if (enableWheel)
            {
                steeringAngle = Input.GetAxis("Steering");
            }
            else
            {
                if (Input.GetKey(KeyCode.A))
                    steeringAngle -= minSteeringBump;
                if (Input.GetKey(KeyCode.D))
                    steeringAngle += minSteeringBump;
            }
        }

        foreach (var driveWheel in driveWheels)
        {
            driveWheel.steerAngle = currentSteeringAngleDegrees;
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
                var steeringAngleText = currentSteeringAngleDegrees.ToString("F7");
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
            var inverseTurningRadius = float.Parse(fileContents);
            var rawSteeringAngle = Utility.InverseTurningRadiusToSteeringAngleDegrees(inverseTurningRadius);
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

    void DrawCenterLinePoint()
    {
        var point = new GameObject("Point");
        point.transform.position = transform.position;
        point.transform.SetParent(centerLine.transform);
    }

    float currentSteeringAngleDegrees
    {
        get
        {
            var steeringAngleDegrees = 90f * steeringAngle * steeringAngleMultiplier;
            return steeringAngleDegrees;
        }
    }

    enum DrivingMode
    {
        Recording,
        Autonomous,
        AutonomousVarianceTest,
        Manual
    }
}
