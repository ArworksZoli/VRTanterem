using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class FPSManager : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        OVRManager.display.displayFrequency = 90.0f;
        OVRManager.foveatedRenderingLevel = 0;

        StartCoroutine(SetGraphicsInSeconds());
    }

    IEnumerator SetGraphicsInSeconds()
    {
        yield return new WaitForSeconds(1f);

        OVRManager.display.displayFrequency = 90.0f;
        OVRManager.foveatedRenderingLevel = 0;

        Unity.XR.Oculus.Utils.foveatedRenderingLevel = 0;
    }
}
