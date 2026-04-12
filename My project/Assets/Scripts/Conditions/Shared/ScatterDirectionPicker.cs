using UnityEngine;

/// <summary>
/// Static utility for picking scatter directions biased away from the group centroid.
/// Used by Scatter actions in both Leader and GOAPBoids conditions.
/// </summary>
public static class ScatterDirectionPicker
{
    /// <summary>
    /// Returns a random horizontal direction biased away from the centroid.
    /// </summary>
    public static Vector3 PickScatterDirection(Vector3 agentPos, Vector3 centroid)
    {
        Vector3 awayFromCenter = agentPos - centroid;
        awayFromCenter.y = 0f;

        if (awayFromCenter.sqrMagnitude < 0.01f)
            awayFromCenter = Random.insideUnitSphere;

        awayFromCenter.Normalize();

        // Add random angular offset (up to ±90 degrees) for chaotic dispersal
        float randomAngle = Random.Range(-90f, 90f);
        Quaternion rotation = Quaternion.Euler(0f, randomAngle, 0f);
        Vector3 scatterDir = rotation * awayFromCenter;
        scatterDir.y = 0f;

        return scatterDir.normalized;
    }
}
