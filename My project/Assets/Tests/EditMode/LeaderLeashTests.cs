using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for the leader leash formula in LeaderGoapBrain.Update().
/// When the leader drifts beyond maxLeaderSeparation from the flock centroid,
/// its velocity is scaled down so followers can catch up. Applied unconditionally
/// (no longer gated on isMovementOverridden — see Bug 4 fix).
/// </summary>
[TestFixture]
public class LeaderLeashTests
{
    // Mirrors the leash formula from LeaderGoapBrain.Update()
    private static float ComputeLeashFactor(float dist, float maxSep)
    {
        if (dist <= maxSep)
            return 1f;
        float excess = dist - maxSep;
        return Mathf.Clamp01(1f - excess / maxSep);
    }

    private static Vector3 ApplyLeash(Vector3 velocity, float dist, float maxSep)
    {
        return velocity * ComputeLeashFactor(dist, maxSep);
    }

    // ── Within leash range — no slowdown ──

    [Test]
    public void WithinMaxSeparation_FactorIsOne()
    {
        float factor = ComputeLeashFactor(dist: 8f, maxSep: 12f);
        Assert.AreEqual(1f, factor, 0.001f,
            "Leader within leash range should not be slowed");
    }

    [Test]
    public void ExactlyAtMaxSeparation_FactorIsOne()
    {
        float factor = ComputeLeashFactor(dist: 12f, maxSep: 12f);
        Assert.AreEqual(1f, factor, 0.001f,
            "Leader exactly at the leash boundary should not be slowed");
    }

    // ── Beyond leash range — proportional slowdown ──

    [Test]
    public void TwoTimesMaxSeparation_FactorIsZero()
    {
        // dist=24, max=12 → excess=12 → factor = 1 - 12/12 = 0
        float factor = ComputeLeashFactor(dist: 24f, maxSep: 12f);
        Assert.AreEqual(0f, factor, 0.001f,
            "At double the max separation the leader should be fully stopped");
    }

    [Test]
    public void HalfwayBeyondMax_FactorIsHalf()
    {
        // dist=18, max=12 → excess=6 → factor = 1 - 6/12 = 0.5
        float factor = ComputeLeashFactor(dist: 18f, maxSep: 12f);
        Assert.AreEqual(0.5f, factor, 0.001f,
            "Halfway past the leash boundary should halve the velocity");
    }

    [Test]
    public void FarBeyondMax_FactorClampedToZero()
    {
        // Should never go negative
        float factor = ComputeLeashFactor(dist: 1000f, maxSep: 12f);
        Assert.GreaterOrEqual(factor, 0f,
            "Leash factor must never be negative");
        Assert.AreEqual(0f, factor, 0.001f,
            "Very far leader should be fully stopped");
    }

    // ── Velocity application ──

    [Test]
    public void WithinRange_VelocityUnchanged()
    {
        Vector3 original = new Vector3(5f, 0f, 3f);
        Vector3 result = ApplyLeash(original, dist: 5f, maxSep: 12f);
        Assert.AreEqual(original, result,
            "Velocity must not change when leader is within leash range");
    }

    [Test]
    public void TwiceRange_VelocityBecomesZero()
    {
        Vector3 original = new Vector3(8f, 0f, 0f);
        Vector3 result = ApplyLeash(original, dist: 24f, maxSep: 12f);
        Assert.AreEqual(0f, result.magnitude, 0.001f,
            "Velocity should be zero when leader is at twice the max separation");
    }

    [Test]
    public void BeyondRange_VelocityDirectionPreserved()
    {
        // When slowed, the direction of travel should not change — only magnitude reduces
        Vector3 original = new Vector3(6f, 0f, 8f); // normalized = (0.6, 0, 0.8)
        Vector3 result = ApplyLeash(original, dist: 18f, maxSep: 12f); // factor = 0.5

        if (result.sqrMagnitude > 0.001f)
        {
            Vector3 originalDir = original.normalized;
            Vector3 resultDir = result.normalized;
            Assert.AreEqual(originalDir.x, resultDir.x, 0.001f, "Direction X preserved");
            Assert.AreEqual(originalDir.z, resultDir.z, 0.001f, "Direction Z preserved");
        }
    }

    // ── Default maxLeaderSeparation value ──

    [Test]
    public void DefaultValue_8Units_AllowsNormalLeadership()
    {
        // A 6-unit lead (well within 8) should produce zero slowdown.
        // Default lowered from 12 → 8 on 2026-05-06 after the centroid-dilution
        // fix exposed that the leader was sustainably exceeding the previous
        // 1.5×-leash bound (18 m) with the corrected follower-only measurement.
        float factor = ComputeLeashFactor(dist: 6f, maxSep: 8f);
        Assert.AreEqual(1f, factor, 0.001f,
            "Default 8-unit leash should let leader maintain a 6-unit lead freely");
    }

    [Test]
    public void DefaultValue_8Units_HardStopsAt16()
    {
        // 2× default (16 m) is the formula's full-stop point. Integration test
        // bound is 18 m, leaving 2 m of buffer for brief frame-by-frame overshoot.
        float factor = ComputeLeashFactor(dist: 16f, maxSep: 8f);
        Assert.AreEqual(0f, factor, 0.001f,
            "At 2× default leash the leader should be fully stopped");
    }
}
