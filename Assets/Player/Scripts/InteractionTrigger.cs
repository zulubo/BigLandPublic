using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class InteractionTrigger : MonoBehaviour
{
    public const int interactionLayer = 6;
    public static readonly int interactionLayerMask = 1 << interactionLayer;
    private void OnEnable()
    {
        gameObject.layer = interactionLayer;
    }

    public UnityEngine.Events.UnityEvent OnInteract;

    public void Interact()
    {
        OnInteract.Invoke();
    }
}
