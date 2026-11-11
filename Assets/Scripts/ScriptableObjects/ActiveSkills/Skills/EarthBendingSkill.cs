using Equipment;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace ActiveSkills
{
    /// <summary>
    /// Raises a stone wall at the cursor that blocks every unit and takes enemy attacks and projectiles until it breaks.
    /// Your own attacks pass through it. Casting it again replaces the wall.
    /// </summary>
    [CreateAssetMenu(fileName = "EarthBending", menuName = "Active Skills/Earth Bending")]
    public class EarthBendingSkill : ActiveSkill<EarthBending>
    {
        [SerializeField] float cooldown = 8f;
        [SerializeField] float resourceCost = 15f;
        [Tooltip("Furthest it can be raised from you, in meters")]
        public float Range = 12f;
        [Tooltip("The wall's health as a share of your max health: 0.5 = 50%")]
        public float HealthShare = 0.5f;
        [Tooltip("Meters closer enemies treat it as when picking a target: negative, so they prefer players")]
        public float Threat = -6f;

        [Header("Size, all scaled by area of effect")]
        [Min(0.1f)] public float Width = 6f;
        [Min(0.1f)] public float Height = 3f;
        [Min(0.1f)] public float Thickness = 1f;

        public override SkillType Type => SkillType.EarthBending;
        public override float Cooldown => cooldown;
        public override float ResourceCost => resourceCost;

        // The walls' collider, shared by every wall raised with this asset's size (blobs aren't freed while walls may use them)
        BlobAssetReference<Unity.Physics.Collider> _collider;
        float3 _colliderSize;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Affix.Health)).Text($"Wall health: {Pct(HealthShare)} of your max health"));
            description.Effects.Add(AreaLine("Wall width", Width, stats));
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Distance), "Range", Range, null, " m"));
            description.Effects.Add(new SkillLine().Text("Blocks enemies and their attacks; yours pass through"));
            description.Effects.Add(new SkillLine().Text("Casting it again replaces the wall"));
        }

        protected override EarthBending Configure(EarthBending component, bool isNew)
        {
            component.Cooldown = cooldown;
            component.ResourceCost = resourceCost;
            component.Range = Range;
            component.HealthShare = HealthShare;
            component.Threat = Threat;
            if (isNew) component.Elapsed = cooldown;  // Ready as soon as it's equipped
            return component;
        }

        public override void Apply(EntityManager entityManager, Entity entity)
        {
            base.Apply(entityManager, entity);

            var bending = entityManager.GetComponentData<EarthBending>(entity);
            if (!entityManager.Exists(bending.Prefab)) bending.Prefab = entityManager.CreateEntity();
            SetUpPrefab(entityManager, bending.Prefab, entity);
            entityManager.SetComponentData(entity, bending);
        }

        public override void Remove(EntityManager entityManager, Entity entity)
        {
            if (entityManager.HasComponent<EarthBending>(entity))
            {
                var prefab = entityManager.GetComponentData<EarthBending>(entity).Prefab;
                if (entityManager.Exists(prefab)) entityManager.DestroyEntity(prefab);
            }
            base.Remove(entityManager, entity);
        }

        // One prefab per caster, so a wall copied from it already knows its owner. Also refreshes it when the skill is applied again.
        void SetUpPrefab(EntityManager entityManager, Entity prefab, Entity owner)
        {
            UnitEntitySetup.AddOrSet(entityManager, prefab, LocalTransform.Identity);
            UnitEntitySetup.AddOrSet(entityManager, prefab, new LocalToWorld { Value = float4x4.identity });
            UnitEntitySetup.AddOrSet(entityManager, prefab, new PhysicsCollider { Value = Collider() });
            if (!entityManager.HasComponent<PhysicsWorldIndex>(prefab)) entityManager.AddSharedComponent(prefab, new PhysicsWorldIndex());
            UnitEntitySetup.AddOrSet(entityManager, prefab, new EarthWall { Owner = owner });
            UnitEntitySetup.AddOrSet(entityManager, prefab, new UnitInfo { Source = this });
            UnitEntitySetup.AddCombatComponents(entityManager, prefab, new StatBlock { Health = 1f });
            CompanionEntitySetup.AddTargetable(entityManager, prefab, owner, Threat);
            if (!entityManager.HasComponent<Prefab>(prefab)) entityManager.AddComponent<Prefab>(prefab);
        }

        // A box standing on its position, Width across and Thickness deep. On the Player layer (enemy attacks and
        // projectiles hit it like an ally) and Default (it blocks every unit, like a prop).
        BlobAssetReference<Unity.Physics.Collider> Collider()
        {
            var size = new float3(Width, Height, Thickness);
            if (_collider.IsCreated && math.all(size == _colliderSize)) return _collider;
            _colliderSize = size;
            _collider = Unity.Physics.BoxCollider.Create(
                new BoxGeometry { Center = new float3(0f, Height * 0.5f, 0f), Orientation = quaternion.identity, Size = size, BevelRadius = 0.05f },
                new CollisionFilter { BelongsTo = (uint)CollisionLayerHelper.Player | 1u, CollidesWith = ~0u, GroupIndex = 0 });
            return _collider;
        }
    }
}
