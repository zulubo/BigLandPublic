using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages interactions for the first person character
/// </summary>
public class FirstPersonInteractor : MonoBehaviour
{
    public static FirstPersonInteractor instance;

    public FirstPersonInteraction currentInteraction { get; private set; }
    public bool isInteracting => currentInteraction != null;

    public RigidbodyCharacterController characterController;
    [SerializeField] LayerMask raycastMask;
    [SerializeField] float raycastDistance = 1.0f;

    [SerializeField] KeyCode interactionKey = KeyCode.E;

    public new Camera camera;
    public Vector3 cameraRestPos { get; private set; }

    private void Start()
    {
        cameraRestPos = camera.transform.localPosition;
        instance = this;
    }
    private void OnDestroy()
    {
        if(instance == this) instance = null;
    }

    private void Update()
    {
        if (!isInteracting) 
        {
            if (Input.GetKeyDown(interactionKey)
                && Physics.Raycast(new Ray(camera.transform.position, camera.transform.forward), out RaycastHit hit,
                raycastDistance, raycastMask))
            {
                if (hit.collider.TryGetComponent(out InteractionTrigger trigger) && trigger.enabled)
                {
                    trigger.Interact();
                }
            }
        }
    }

    public void BeginInteraction(FirstPersonInteraction i)
    {
        characterController.enabled = false;
        characterController.rigidbody.detectCollisions = false;
        characterController.rigidbody.isKinematic = true;
        characterController.transform.parent = i.localSpace;
        currentInteraction = i;
    }

    public void EndInteraction(FirstPersonInteraction i)
    {
        if (currentInteraction == i) currentInteraction = null;
        characterController.enabled = true;
        characterController.rigidbody.detectCollisions = true;
        characterController.rigidbody.isKinematic = false;
        characterController.transform.parent = null;
    }
}
