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
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
