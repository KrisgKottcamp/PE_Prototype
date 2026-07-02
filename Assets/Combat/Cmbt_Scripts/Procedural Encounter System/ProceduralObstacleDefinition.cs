using UnityEngine;

[CreateAssetMenu(
    menuName = "Game/Combat/Procedural Obstacle Definition",
    fileName = "ProceduralObstacle"
)]
public class ProceduralObstacleDefinition : ScriptableObject
{
    [Header("Prefab")]
    public GameObject prefab;

    [Header("Placement Footprint")]
    [Tooltip(
        "Conservative circular footprint used for placement and spacing. " +
        "It should fully contain the obstacle collider."
    )]
    [Min(0.05f)]
    public float footprintRadius = 1f;

    [Tooltip(
        "Additional empty space required around this obstacle."
    )]
    [Min(0f)]
    public float extraSpacing = 0.2f;

    [Header("Allowed Rotation")]
    [Tooltip(
        "Allowed Z rotations. Use values such as 0, 90, 180, and 270. " +
        "Leave one element at 0 for obstacles that should never rotate."
    )]
    public float[] allowedZRotations = new float[] { 0f };

    [Header("Tactical Point Expectations")]
    [Tooltip(
        "Enable this when the prefab is expected to contain child " +
        "CombatTacticalPoint components."
    )]
    public bool expectsTacticalPoints = true;

    [Min(0)]
    public int minimumTacticalPointCount = 1;

    public float GetRandomRotation(System.Random random)
    {
        if (allowedZRotations == null ||
            allowedZRotations.Length == 0)
        {
            return 0f;
        }

        int index = random.Next(
            0,
            allowedZRotations.Length
        );

        return allowedZRotations[index];
    }

    private void OnValidate()
    {
        footprintRadius =
            Mathf.Max(0.05f, footprintRadius);

        extraSpacing =
            Mathf.Max(0f, extraSpacing);

        minimumTacticalPointCount =
            Mathf.Max(0, minimumTacticalPointCount);
    }
}
