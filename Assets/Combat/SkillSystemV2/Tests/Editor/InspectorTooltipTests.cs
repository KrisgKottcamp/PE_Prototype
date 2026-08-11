using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ProjectEri.SkillSystemV2.Tests
{
    public sealed class InspectorTooltipTests
    {
        [Test]
        public void DesignerEditableSpellFields_HavePlainEnglishTooltips()
        {
            Assembly assembly = typeof(SpellDefinition).Assembly;
            var types = new HashSet<Type>
            {
                typeof(SpellDefinition),
                typeof(SpellTiming),
                typeof(SpellResourceCost),
                typeof(TargetFilter),
                typeof(SpellDeliverySlot),
                typeof(SpellEffectSlot),
                typeof(SpellEventEffectRoute),
                typeof(SpellReactiveEffectGroup),
                typeof(DeliveryInteractionFilter),
                typeof(SpellReactionSlot),
                typeof(DamageTypeDefinition),
                typeof(GameplayResourceDefinition),
                typeof(GameplaySignalDefinition),
                typeof(StatusDefinition)
            };

            foreach (Type type in assembly.GetTypes())
            {
                if (type.IsAbstract)
                    continue;

                if (typeof(SpellDeliverySettings).IsAssignableFrom(type) ||
                    typeof(SpellEffectSettings).IsAssignableFrom(type) ||
                    typeof(DeliveryDefinition).IsAssignableFrom(type) ||
                    typeof(EffectDefinition).IsAssignableFrom(type) ||
                    typeof(PlayerTargetingDefinition).IsAssignableFrom(type) ||
                    typeof(DeliveryInteractionCondition).IsAssignableFrom(type) ||
                    typeof(DeliveryInteractionResponse).IsAssignableFrom(type))
                {
                    types.Add(type);
                }
            }

            var missing = new List<string>();
            foreach (Type type in types)
            {
                for (Type current = type;
                     current != null && current != typeof(object);
                     current = current.BaseType)
                {
                    FieldInfo[] fields = current.GetFields(
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly);
                    for (int i = 0; i < fields.Length; i++)
                    {
                        FieldInfo field = fields[i];
                        bool serialized = field.IsPublic ||
                            field.GetCustomAttribute<SerializeField>() != null ||
                            field.GetCustomAttribute<SerializeReference>() != null;
                        if (!serialized ||
                            field.GetCustomAttribute<HideInInspector>() != null)
                        {
                            continue;
                        }

                        TooltipAttribute tooltip =
                            field.GetCustomAttribute<TooltipAttribute>();
                        if (tooltip == null ||
                            string.IsNullOrWhiteSpace(tooltip.tooltip))
                        {
                            missing.Add($"{current.Name}.{field.Name}");
                        }
                    }
                }
            }

            Assert.That(
                missing,
                Is.Empty,
                "Designer-editable fields missing tooltips: " +
                string.Join(", ", missing.OrderBy(value => value)));
        }
    }
}
