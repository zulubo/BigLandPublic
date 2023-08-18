using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base class for a first person interaction that takes control of the camera
/// </summary>
public abstract class FirstPersonInteraction : MonoBehaviour
{
    /// <summary>
    /// Is the interaction currently allowed to be started
    /// </summary>
    protected abstract bool InteractionAllowed();
    /// <summary>
    /// Called when an interaction begins
    /// </summary>
    protected abstract void BeginInteraction();
    /// <summary>
    /// Interaction will end once this returns true
    /// </summary>
    protected abstract bool IsInteractionFinished();
    /// <summary>
    /// The camera pose, animated however you like for the interaction. This should be in world space.
    /// </summary>
    protected abstract Pose GetViewPose();
    Pose GetLocalViewPose()
    {
        return ToLocalSpace(GetViewPose());
    }
    /// <summary>
    /// A reference frame for the interaction
    /// </summary>
    public abstract Transform localSpace { get; }
    protected Pose ToLocalSpace(Pose p)
    {
        return new Pose(localSpace.InverseTransformPoint(p.position), Quaternion.Inverse(localSpace.rotation) * p.rotation);
    }
    protected Pose FromLocalSpace(Pose p)
    {
        return new Pose(localSpace.TransformPoint(p.position), localSpace.rotation * p.rotation);
    }

    /// <summary>
    /// If returns true, character will be moved to this position and direction when exiting interaction
    /// </summary>
    protected abstract bool FinalCharacterPose(out Vector3 position, out Vector3 forward);

    private FirstPersonInteractor interactor => FirstPersonInteractor.instance;

    public bool interacting { get; private set; }

    public void TriggerInteraction()
    {
        if (!InteractionAllowed()) return;
        BeginInteraction();
        interactor.BeginInteraction(this);
        interactor.StartCoroutine(InteractionCoroutine());
        interacting = true;
    }

    const float interpolateTime = 0.5f;
    IEnumerator InteractionCoroutine()
    {
        Pose camStartPose = new Pose(interactor.camera.transform.position, interactor.camera.transform.rotation);
        camStartPose = ToLocalSpace(camStartPose);

        float i = 0;
        while (i < 1)
        {
            i += Time.deltaTime / interpolateTime;
            Pose camPose = LerpPose(camStartPose, GetLocalViewPose(), Mathf.SmoothStep(0, 1, i));
            interactor.camera.transform.SetPositionAndRotation(localSpace.TransformPoint(camPose.position), 
                localSpace.rotation * camPose.rotation);
            yield return null;
        }

        while (!IsInteractionFinished())
        {
            Pose camPose = GetLocalViewPose();
            interactor.camera.transform.SetPositionAndRotation(localSpace.TransformPoint(camPose.position), 
                localSpace.rotation * camPose.rotation);
            yield return null;
        }

        if(FinalCharacterPose(out Vector3 charPos, out Vector3 charDir))
        {
            interactor.characterController.transform.position = charPos;
            interactor.characterController.transform.rotation = Quaternion.LookRotation(charDir);
        }

        Pose camEndPose = new Pose(interactor.cameraRestPos, Quaternion.identity);

        i = 1;
        while (i > 0)
        {
            i -= Time.deltaTime / interpolateTime;
            Pose camEndPoseTransformed = new Pose(interactor.camera.transform.parent.TransformPoint(camEndPose.position), 
                interactor.camera.transform.parent.rotation * camEndPose.rotation);
            Pose camPose = LerpPose(camEndPoseTransformed, GetViewPose(), Mathf.SmoothStep(0, 1, i));
            interactor.camera.transform.SetPositionAndRotation(camPose.position, camPose.rotation);
            yield return null;
        }
        interactor.camera.transform.SetLocalPositionAndRotation(interactor.cameraRestPos, Quaternion.identity);

        interactor.EndInteraction(this);
        interacting = false;
    }

    protected Pose LerpPose(Pose a, Pose b, float t)
    {
        return new Pose(Vector3.Lerp(a.position, b.position, t), Quaternion.Lerp(a.rotation, b.rotation, t));
    }
}
