using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
public class ShipHUD : MonoBehaviour
{
    public Ship ship;
    [SerializeField] GameObject hudRoot;
    [SerializeField] Transform gyro;
    [SerializeField] TMP_Text yVel;
    [SerializeField] HUDLinearMeter altimeter;
    [SerializeField] HUDLinearMeter xVel;
    [SerializeField] HUDLinearMeter zVel;

    private void Update()
    {
        hudRoot.SetActive(ship.isBeingPiloted);
        if(ship.isBeingPiloted) UpdateHUD();
    }

    void UpdateHUD()
    {
        Vector3 gyroRot = -ship.transform.eulerAngles;
        gyroRot.y = 0;
        gyroRot.x = Mathf.Repeat(gyroRot.x + 90, 180) - 90;
        gyro.transform.localRotation = Quaternion.identity;
        gyro.transform.Rotate(0, 0, gyroRot.z, Space.Self);
        gyro.transform.Rotate(gyroRot.x, 0, 0, Space.Self);

        Vector3 localVel = Quaternion.Euler(0, -ship.transform.eulerAngles.y, 0) * ship.rigidbody.velocity;
        yVel.text = Mathf.RoundToInt(localVel.y).ToString();
        altimeter.value = ship.transform.position.y;
        xVel.value = localVel.x;
        zVel.value = localVel.z;
    }
}
