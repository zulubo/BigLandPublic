using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LadderInteraction : FirstPersonInteraction
{
    public Transform startPt;
    public Transform endPt;
    public Transform dismountPt;
    public float duration = 2f;

    bool doneTraversing;
    float traversalT;
    IEnumerator TraverseLadder()
    {
        doneTraversing = false;
        traversalT = 0;
        while(traversalT < 1)
        {
            traversalT += Time.deltaTime / duration;
            yield return null;
        }
        traversalT = 1;
        doneTraversing = true;
    }

    public override Transform localSpace => transform;

    override protected bool InteractionAllowed() => true;
    override protected void BeginInteraction() 
    {
        StartCoroutine(TraverseLadder());
    }

    override protected Pose GetViewPose()
    {
        return new Pose(Vector3.Lerp(startPt.position, endPt.position, traversalT), Quaternion.Lerp(startPt.rotation, endPt.rotation, traversalT));
    }
    override protected bool IsInteractionFinished() => doneTraversing;

    override protected bool FinalCharacterPose(out Vector3 position, out Vector3 forward)
    {
        position = dismountPt.position;
        forward = dismountPt.forward;
        return true;
    }
}
