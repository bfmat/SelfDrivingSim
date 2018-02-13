using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

sealed class SignBoxManager : MonoBehaviour
{
    // The path to read stop sign positions from
    const string stopSignPositionPath = Utility.tmpPath + "sign_positions.csv";

    // The bounding boxes placed on the UI canvas that mark the locations of stop signs
    [SerializeField] GameObject boundingBox;

    // The UI canvas on which the bounding boxes are placed
    GameObject uiCanvas;
    // The size of the UI canvas's rectangle transform
    Vector2 uiCanvasSize;
    // The initial scale of the bounding box prefab
    Vector3 initialBoxScale;

    // Initialization function in which we find the UI canvas and get the relevant components
    void Start()
    {
        // Find the UI canvas and set the global variable
        uiCanvas = GameObject.FindGameObjectWithTag("UICanvas");
	// Check if it is null; if it is, destroy this script and exit
	if (uiCanvas == null)
        {
            Destroy(this);
            return;
        }
        // Get the size of the canvas's transform
        uiCanvasSize = uiCanvas.GetComponent<RectTransform>().sizeDelta;
        // Get the box's starting scale and remember it for later use
        initialBoxScale = boundingBox.transform.localScale;
    }

    // Update function in which we update bounding boxes in the UI that mark the positions of stop signs
    void Update()
    {
        // If the file exists at all
        if (File.Exists(stopSignPositionPath))
        {
            // Destroy all child objects of the UI canvas
            foreach (Transform childTransform in uiCanvas.transform)
            {
                Destroy(childTransform.gameObject);
            }

            // For each of the lines in the file
            foreach (var line in File.ReadAllLines(stopSignPositionPath))
            {
                // Split the string by a comma and convert the two elements to floating point numbers that are centered at 0
                // The vertical position must be negative because in Unity, greater numbers are up instead of down (as in NumPy and OpenCV)
                var stringValues = line.Split(',');
                var x = float.Parse(stringValues[0]) - 0.5f;
                var y = -(float.Parse(stringValues[1]) - 0.5f);
                // Instantiate a bounding box and set it as a child of the UI canvas
                var box = Instantiate(boundingBox);
                var boxRectTransform = box.GetComponent<RectTransform>();
                boxRectTransform.SetParent(uiCanvas.transform);
                // Set its position to the X and Y values multiplied by the corresponding dimensions of the UI canvas
                boxRectTransform.anchoredPosition = new Vector2(x * uiCanvasSize.x, y * uiCanvasSize.y);
                // Get the scale corresponding to its vertical position, and set the box's scale accordingly
                var scale = getBoxScale(y);
                boxRectTransform.localScale = scale;
            }
        }
    }

    // Get the scale for the bounding box, given its vertical position on the screen scaled to the range of 0.5 to -0.5
    Vector3 getBoxScale(float verticalPosition)
    {
        // Scale the box exponentially with base 15
        var multiplier = Mathf.Pow(15, verticalPosition);
        // Multiply the box's initial scale by the exponential value to get its new scale
        print(initialBoxScale * multiplier);
        return initialBoxScale * multiplier;
    }
}
