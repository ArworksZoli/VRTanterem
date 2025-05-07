using System;
using UnityEngine;

public class ClockController : MonoBehaviour
{
    [Header("Clock Hands")]
    [Tooltip("Transform of the hour hand.")]
    [SerializeField] private Transform hourHandTransform;

    [Tooltip("Transform of the minute hand.")]
    [SerializeField] private Transform minuteHandTransform;

    [Header("Update Settings")]
    [Tooltip("Should the clock hands update continuously or only per minute/hour change?")]
    [SerializeField] private bool continuousUpdate = true;

    private const float DegreesPerHour = 360f / 12f;
    private const float DegreesPerMinute = 360f / 60f;

    void Update()
    {
        if (hourHandTransform == null || minuteHandTransform == null)
        {
            Debug.LogWarning("[ClockController] Hour or Minute hand transform is not assigned.", this);
            enabled = false;
            return;
        }

        DateTime currentTime = DateTime.Now;

        float hour = currentTime.Hour % 12;
        float minuteForHour = currentTime.Minute;
        float hourAngle = (hour + minuteForHour / 60f) * DegreesPerHour;

        float minute = currentTime.Minute;
        float secondForMinute = continuousUpdate ? currentTime.Second : 0;
        float minuteAngle = (minute + secondForMinute / 60f) * DegreesPerMinute;

        // Lokális X tengely körül
        hourHandTransform.localRotation = Quaternion.Euler(hourAngle, 0f, 0f);
        minuteHandTransform.localRotation = Quaternion.Euler(minuteAngle, 0f, 0f);
    }
}