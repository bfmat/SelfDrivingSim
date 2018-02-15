using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

// The main car class that handles movement and recording
sealed class Car : MonoBehaviour
{
    // The speed that time passes at when in reinforcement learning mode
    const float timeScaleReinforcement = 1f;
    // Torque to constantly apply to the front wheels
    const float torque = 12.5f;
    // During automated tests, spend this many seconds on each individual lane
    const float timeSpentOnLane = 100f;
    // Width and height of saved screenshots
    const int resWidth = 320, resHeight = 180;
    // The amount to increase the steering angle per key press (for keyboard controls)
    const float keyboardSteeringBump = 0.1f;
    // The absolute value which the wheel angle is incremented during a reinforcement learning action
    const float reinforcementWheelAngleBump = 0.1f;
    // The path to write data required by the reinforcement learning agent to
    const string reinforcementInformationPath = Utility.tmpPath + "information.json";
    // The path to read actions calculated by the reinforcement learning agent from
    const string reinforcementActionPath = Utility.tmpPath + "action.txt";
    // The path to the file whose existence instructs the simulation to reset the car to its starting point when in evolutionary autonomous mode
    const string evolutionaryResetPath = Utility.tmpPath + "reset_sim";
    // The distance that the car must be away from the center line for it to be considered a failure
    const float failureThreshold = 4f;
    // Whether or not to use backlash in steering 
    const bool useBacklash = true;
    // The time to wait after reading a steering angle file, to simulate the slower framerate of the real car
    const float fileReadingDelay = 0.1f;

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
    // The angle in degrees to set the angle of the drive wheels to
    float wheelAngle = 0f;
    // Are we currently saving screenshots?
    bool currentlyRecording;
    // Physics body of the car
    Rigidbody rb;
    // List of past squared errors during automated testing
    List<float> squaredErrors = new List<float>();

    // Main initialization function
    void Start()
    {
        // Get the robot's rigidbody and store it in a global variable
        rb = GetComponent<Rigidbody>();

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

            // If we are testing the car's standard deviation from the center line
            else if (drivingMode == DrivingMode.AutonomousVarianceTest)
            {
                // Set the car's position to the first lane and continue to switch lanes in the future
                SwitchLanes(true);
                // Set the flag so that errors will be tracked and recorded
                trackErrors = true;
                // Start automatic control of the steering angle
                StartCoroutine(HandleAutonomousSteering());
            }

            // If we are in autonomous mode for use with a reinforcement learning agent
            else if (drivingMode == DrivingMode.AutonomousReinforcement)
            {
                // Set the speed that time passes at
                Time.timeScale = timeScaleReinforcement;
                // Set the car's position to the first lane but do not repeat
                SwitchLanes(false);
                // Start control of the steering angle by the agent
                StartCoroutine(HandleReinforcementSteering());
            }

            // If we are in autonomous mode with the evolutionary algorithm agent
            else if (drivingMode == DrivingMode.AutonomousEvolutionary)
            {
                // Set the car's position to the first lane but do not repeat
                SwitchLanes(false);
                // Start automatic control of the steering angle
                StartCoroutine(HandleAutonomousSteering());
                // Reset the car if it falls off the road or if it is instructed to by the agent
                StartCoroutine(EvolutionaryReset());
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

        // Set the global variable containing the number of lanes
        numLanes = centerLinePointCollections.Length;

        // If either the wheel is disabled, or we are not in recording mode, enable recording right away (this is so that we do not have to press a button to go into autonomous driving)
        currentlyRecording = (drivingMode != DrivingMode.Recording) || !enableWheel;

        // Set each of the drive wheels' torque to the predefined torque value
        foreach (var driveWheel in driveWheels)
            driveWheel.motorTorque = torque;
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
                // Set the wheel angle using the steering wheel's input
                wheelAngle = Input.GetAxis("Steering");
            }
            // Otherwise if the steering is disabled
            else
            {
                // If the A key is pressed, move the steering angle to the left
                if (Input.GetKey(KeyCode.A))
                    wheelAngle -= keyboardSteeringBump;
                // If the D key is pressed, move the steering angle to the right
                if (Input.GetKey(KeyCode.D))
                    wheelAngle += keyboardSteeringBump;
            }
        }

        // Set each of the drive wheels' steering angles to the global wheel angle
        foreach (var driveWheel in driveWheels)
            driveWheel.steerAngle = wheelAngle;

        // Get the value from the buttons that tell the car to start or stop recording
        var recordingButton = Input.GetAxisRaw("EnableRecording");
        // If the positive button is pressed, recording must be enabled
        if (recordingButton > 0)
            currentlyRecording = true;
        // If the negative button is pressed, recording must be disabled
        else if (recordingButton < 0)
            currentlyRecording = false;

        // If the car should currently be tracking errors
        if (trackErrors)
        {
            // Call the function to calculate the center line error 
            var squaredError = Utility.CalculateCenterLineError(centerLinePoints, transform.position);
            // Add it to the list of squared errors
            squaredErrors.Add(squaredError);
        }
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
                var wheelAngleText = wheelAngle.ToString("F7");
                // Save the file in the `sim` subfolder of the project folder
                fileName = "sim/" + unixTimestamp + "_" + wheelAngleText + ".png";
            }
            // Otherwise, save the file in the temp folder
            else
            {
                fileName = Utility.tmpPath + "temp.png";
            }
            // Write the contents of the image to a file
            File.WriteAllBytes(fileName, encodedImage);
            // If we are not recording, move the temp file to a permanent numbered filename
            // This is done so that the files are never read by other programs when partially written
            if (drivingMode != DrivingMode.Recording)
            {
                File.Move(Utility.tmpPath + "temp.png", Utility.tmpPath + "sim" + i + ".png");
            }

	    // Unload unused assets to get rid of unreferenced objects and prevent memory leaks
	    Resources.UnloadUnusedAssets();

            // Wait until the next frame to save another image
            yield return new WaitForEndOfFrame();
        }
    }

    // Coroutine that manages autonomous steering in the background
    IEnumerator HandleAutonomousSteering()
    {
        // There are text files in the temp folder with names of the format <temp directory>/#sim.txt
        // Store the sim.txt suffix in a constant
        const string fileSuffix = "sim.txt";
        // Calculate the starting index of the number in these files' full paths
        int startingIndex = Utility.tmpPath.Length;
        // Calculate the length of all parts of the path except for the number, used to calculate the length of the number during the substring operation
        int pathLengthExcludingNumber = startingIndex + fileSuffix.Length;

        // Loop forever, parallel to the main thread
        while (true)
        {
            // If the car is not in manual or recording mode
            if (drivingMode != DrivingMode.Manual && drivingMode != DrivingMode.Recording)
            {
                // The file of the above format with the greatest numeric prefix needs to be found
                // Get the names of all of these files and convert the numeric prefixes to integers, choosing the maximum (most recent) file index
                var maxIndex = (
                    from path in Directory.GetFiles(Utility.tmpPath)
                    where path.Contains(fileSuffix)
                    select int.Parse(path.Substring(startingIndex, path.Length - pathLengthExcludingNumber))
                ).Max();

                // Create a stream reader and read the corresponding file's entire contents
                var streamReader = new StreamReader(Utility.tmpPath + maxIndex + fileSuffix);
                var fileContents = streamReader.ReadToEnd();
                // Convert the file contents to a decimal number which represents the steering angle
                var steeringAngle = float.Parse(fileContents);
                // Convert the steering angle to a wheel angle
                wheelAngle = Steering.getWheelAngle(steeringAngle, useBacklash);
            }
            // Wait for a predefined delay to simulate the limited framerate of the real car
            yield return new WaitForSeconds(fileReadingDelay);
        }
    }

    // Coroutine that manages steering and communication with the reinforcement learning agent
    IEnumerator HandleReinforcementSteering()
    {
        // Loop forever, parallel to the main thread
        while (true)
        {
            // Read the contents of the action file path
            var actionFileContents = File.ReadAllText(reinforcementActionPath);
            // Strip whitespace and attempt to convert the file's contents to an integer
            int action;
            var success = int.TryParse(actionFileContents.Trim(), out action);
            // If parsing the file as an integer succeeded
            if (success)
            {
                // The action should be 0, 1, or 2
                // If it is 0, set the steering angle to 0
                if (action == 0)
                {
                    wheelAngle = 0f;
                }
                // Otherwise, it is 1 or 2
                else
                {
                    // Get the sign of the corresponding steering angle
                    var sign = (action == 1) ? -1 : 1;
                    // If the sign of the wheel angle is presently the opposite of the calculated sign, set it to 0
                    if (Mathf.Sign(wheelAngle) != sign)
                    {
                        wheelAngle = 0f;
                    }
                    // Multiply the sign by the absolute increment to get the signed increment, and add it to the wheel angle
                    wheelAngle += (reinforcementWheelAngleBump * sign);
                }

                // If the previous information has been read and deleted
                if (!File.Exists(reinforcementInformationPath))
                {
                    // Calculate the present squared error from the center line
                    var squaredError = Utility.CalculateCenterLineError(centerLinePoints, transform.position);
                    // Use the negative squared error plus 1 as the reward function, so it will be positive when the car is within 1 unit of the road, and negative as it approaches the edges of the road
                    var reward = -squaredError + 1f;

                    // If the squared error is in excess of the predefined threshold, end the game and teleport the car back to the starting point
                    var done = squaredError > failureThreshold;
                    if (done)
                    {
                        // Go back to the starting point and reset the velocity
                        ResetToStartingPoint(true);
                    }

                    // Create a JSON list out of the wheel angle, the reward, and whether or not the game has ended (using lowercase for the Boolean)
                    var jsonData = "[" + wheelAngle + "," + reward + "," + done.ToString().ToLowerInvariant() + "]";
                    // Write the data to the information path
                    File.WriteAllText(reinforcementInformationPath, jsonData);
                }
            }
            // Wait for the next fixed update
            yield return new WaitForFixedUpdate();
        }
    }

    // Coroutine that monitors the car's distance from the center line and resets if it exceeds a certain value, or if it is instructed to reset by the evolutionary driving agent
    IEnumerator EvolutionaryReset()
    {
        // Loop forever
        while (true)
        {
            // If the reset file exists
            if (File.Exists(evolutionaryResetPath))
            {
                // Reset to the beginning
                ResetToStartingPoint(false);
                // Delete the reset file
                File.Delete(evolutionaryResetPath);
            }
            // Wait for the next fixed update
            yield return new WaitForFixedUpdate();
        }
    }

    // Function for switching to the next lane during autonomous testing mode
    void SwitchLanes(bool repeat)
    {
        // If we are not switching into the first lane, save the test results so far
        if (currentLane > 0)
            Utility.SaveTestResults(squaredErrors, currentLane);

        // If we have not exceeded the maximum number of lanes
        if (currentLane <= numLanes)
        {
            // Set the center line points to the collection corresponding to the next lane
            centerLine = centerLinePointCollections[currentLane];
            // Restart at the beginning of the next lane
            ResetToStartingPoint(false);
            // Increment the current lane number
            currentLane++;

            // If the lane switching must repeat
            if (repeat)
            {
                // Switch lanes once a certain amount of time has passed
                Invoke("SwitchLanes", timeSpentOnLane);
            }
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

    // Teleport the car back to the starting point and reset the global array of center line points
    void ResetToStartingPoint(bool randomPoint)
    {
        // Get all child points of the center line
        var centerLineTransforms = centerLine.GetComponentsInChildren<Transform>();
        // Skip the first element (which corresponds to the parent center line object itself) and convert the rest of the transforms to an array
        var centerLinePointObjects = centerLineTransforms.Skip(1).Distinct().ToArray();

        // Use a random index for the starting point, or simply use 0, depending on the argument
        var randomIndex = randomPoint ? UnityEngine.Random.Range(0, centerLinePointObjects.Length) : 0;
        // Get the point on the center line corresponding to the index
        var startingPointTransform = centerLinePointObjects[randomIndex];
        // Set the car's position to that of the initial point
        transform.position = startingPointTransform.position;

        // Project each of the points' 3D positions onto a 2D plane and store them in the global array
        centerLinePoints = (
            from centerLinePointObject in centerLinePointObjects
            select Utility.ProjectOntoXZPlane(centerLinePointObject.transform.position)
        ).ToArray();

        // Get the delta (ignoring the Y axis) from the starting point to the next point
        var nextPointIndex = (randomIndex + 1) % centerLinePoints.Length;
        var delta = centerLinePoints[nextPointIndex] - centerLinePoints[randomIndex];
        // Convert the top-down 2D delta vector to a 3D vector
        var delta3D = new Vector3(delta.x, 0f, delta.y);
        // Set the car's rotation to look in the direction of the delta vector
        transform.rotation = Quaternion.LookRotation(delta3D);

        // Set the car's velocity to zero to prevent it from carrying over momentum from the previous lane
        rb.velocity = Vector3.zero;
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
        AutonomousVarianceTest,
        // Autonomous mode, using the reinforcement learning agent to learn and drive
        AutonomousReinforcement,
        // Autonomous mode, using evolutionary algorithms to train a neural network that steers the car
        AutonomousEvolutionary
    }
}
