// Final Assignment
// Step 2: Patient Interaction Game Mouse-Movement


using UnityEngine;

// Attach this to the Patient_01 GameObject to enable mouse-driven
// rotation about pivot (like a globe) (left-click drag) and translation of position (right-click drag).
public class PatientInteraction : MonoBehaviour
{
    public float Speed = 5f;
    public Camera mainCamera;
    private float cameraZDistance;
    // right-drag state
    private bool isRightDragging = false;
    private Vector3 rightDragOffset = Vector3.zero;
    private Plane rightDragPlane;
    // rotation-around-center state
    private Vector3 rotationCenter;
    private bool rotationCenterInitialized = false;
    public bool recomputeCenterAtStart = true;

    void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera != null)
            cameraZDistance = mainCamera.WorldToScreenPoint(transform.position).z;
        else
            Debug.LogWarning("PatientInteraction: mainCamera not assigned and Camera.main is null.");         
    }

    // compute combined renderer bounds center for rotation
    private void ComputeRotationCenter()
    {
        var rends = GetComponentsInChildren<Renderer>();
        if (rends == null || rends.Length == 0)
        {
            rotationCenter = transform.position;
            rotationCenterInitialized = true;
            return;
        }

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; ++i)
            b.Encapsulate(rends[i].bounds);

        rotationCenter = b.center;
        rotationCenterInitialized = true;
    }

    void Update()
    {
        // Ensure camera reference (recover if Camera.main becomes available later)
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera != null)
                cameraZDistance = mainCamera.WorldToScreenPoint(transform.position).z;
        }

        // Mouse left click: rotate object like a globe around computed centroid
        if (Input.GetMouseButton(0)) // Mouse left click
        {
            // ensure rotation center is available
            if (!rotationCenterInitialized && recomputeCenterAtStart)
                ComputeRotationCenter();

            float dx = Input.GetAxis("Mouse X");
            float dy = Input.GetAxis("Mouse Y");

            // scale rotation amount; tune multiplier if needed
            float angleX = dx * Speed * 100f * Time.deltaTime; // horizontal drag -> rotate around camera up
            float angleY = dy * Speed * 100f * Time.deltaTime; // vertical drag -> rotate around camera right

            Vector3 camUp = (mainCamera != null) ? mainCamera.transform.up : Vector3.up;
            Vector3 camRight = (mainCamera != null) ? mainCamera.transform.right : Vector3.right;

            // Rotate around the computed center to behave like a globe
            transform.RotateAround(rotationCenter, camUp, angleX);
            transform.RotateAround(rotationCenter, camRight, -angleY);
        }

        // Right-drag begin: capture offset so object doesn't jump
        if (Input.GetMouseButtonDown(1))
        {
            if (mainCamera == null)
                mainCamera = Camera.main;
            if (mainCamera == null)
                return;

            rightDragPlane = new Plane(mainCamera.transform.forward, transform.position);
            Ray downRay = mainCamera.ScreenPointToRay(Input.mousePosition);
            float enterDown;
            if (rightDragPlane.Raycast(downRay, out enterDown))
            {
                Vector3 hitPointDown = downRay.GetPoint(enterDown);
                rightDragOffset = transform.position - hitPointDown;
                isRightDragging = true;
            }
        }

        // Right-drag end
        if (Input.GetMouseButtonUp(1))
        {
            isRightDragging = false;
        }

        // Right-drag move: follow mouse but keep initial offset
        if (isRightDragging && Input.GetMouseButton(1))
        {
            if (mainCamera == null)
                mainCamera = Camera.main;
            if (mainCamera == null)
                return;

            Ray dragRay = mainCamera.ScreenPointToRay(Input.mousePosition);
            float enterDrag;
            if (rightDragPlane.Raycast(dragRay, out enterDrag))
            {
                Vector3 hitPoint = dragRay.GetPoint(enterDrag);
                transform.position = hitPoint + rightDragOffset;
            }
        }
    }
}
