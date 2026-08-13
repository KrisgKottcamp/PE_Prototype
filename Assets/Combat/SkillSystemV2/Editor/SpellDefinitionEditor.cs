using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ProjectEri.SkillSystemV2.Editor
{
    [CustomEditor(typeof(SpellDefinition))]
    [CanEditMultipleObjects]
    public sealed class SpellDefinitionEditor : UnityEditor.Editor
    {
        private SerializedProperty displayName;
        private SerializedProperty stableId;
        private SerializedProperty description;
        private SerializedProperty icon;
        private SerializedProperty category;
        private SerializedProperty timing;
        private SerializedProperty cooldown;
        private SerializedProperty resourceCost;
        private SerializedProperty targetFilter;
        private SerializedProperty placementRules;
        private SerializedProperty aiAffordance;
        private SerializedProperty deliverySlot;
        private SerializedProperty effectSlots;
        private SerializedProperty eventEffectRoutes;
        private SerializedProperty reactiveEffectGroups;
        private SerializedProperty reactionSlots;
        private SerializedProperty maximumChainDepth;
        private SerializedProperty maximumRootActivations;

        private readonly List<SpellValidationIssue> issues =
            new List<SpellValidationIssue>();

        private void OnEnable()
        {
            for (int i = 0; i < targets.Length; i++)
            {
                var spell = targets[i] as SpellDefinition;
                if (spell != null)
                {
                    bool changed = spell.EnsureDeliverySlot();
                    changed |= spell.EnsureEffectSlots();
                    changed |= spell.EnsureEventEffectRoutes();
                    changed |= spell.EnsureReactiveEffectGroups();
                    if (changed)
                        EditorUtility.SetDirty(spell);
                }
            }

            displayName = serializedObject.FindProperty("displayName");
            stableId = serializedObject.FindProperty("stableId");
            description = serializedObject.FindProperty("description");
            icon = serializedObject.FindProperty("icon");
            category = serializedObject.FindProperty("category");
            timing = serializedObject.FindProperty("timing");
            cooldown = serializedObject.FindProperty("cooldown");
            resourceCost = serializedObject.FindProperty("resourceCost");
            targetFilter = serializedObject.FindProperty("targetFilter");
            placementRules = serializedObject.FindProperty("placementRules");
            aiAffordance = serializedObject.FindProperty("aiAffordance");
            deliverySlot = serializedObject.FindProperty("deliverySlot");
            effectSlots = serializedObject.FindProperty("effectSlots");
            eventEffectRoutes = serializedObject.FindProperty(
                "eventEffectRoutes");
            reactiveEffectGroups = serializedObject.FindProperty(
                "reactiveEffectGroups");
            reactionSlots = serializedObject.FindProperty("reactionSlots");
            maximumChainDepth = serializedObject.FindProperty(
                "maximumChainDepth");
            maximumRootActivations = serializedObject.FindProperty(
                "maximumRootActivations");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawIdentity();
            DrawSection("Phase Timing", timing);
            EditorGUILayout.PropertyField(cooldown);
            DrawSection("Resource Cost", resourceCost);
            DrawSection("Target Rules", targetFilter);
            DrawPlacementRules();
            DrawComposition();
            DrawReactions();
            DrawAIGuidance();
            DrawChainSafety();

            serializedObject.ApplyModifiedProperties();
            DrawValidation();
        }

        private void DrawIdentity()
        {
            EditorGUILayout.LabelField(
                "Identity",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(displayName);
            EditorGUILayout.PropertyField(stableId);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button(
                        new GUIContent(
                            "Generate Stable ID",
                            "Create a new permanent unique ID for this spell. Avoid doing this after saves or other assets reference the current ID."),
                        GUILayout.Width(150f)))
                {
                    for (int i = 0; i < targets.Length; i++)
                    {
                        var spell = (SpellDefinition)targets[i];
                        Undo.RecordObject(spell, "Generate Spell Stable ID");
                        spell.RegenerateStableId();
                        EditorUtility.SetDirty(spell);
                    }

                    serializedObject.Update();
                }
            }

            EditorGUILayout.PropertyField(description);
            EditorGUILayout.PropertyField(icon);
            EditorGUILayout.PropertyField(category);
            EditorGUILayout.Space();
        }

        private static void DrawSection(
            string title,
            SerializedProperty property)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(property, includeChildren: true);
            EditorGUILayout.Space();
        }

        private void DrawPlacementRules()
        {
            EditorGUILayout.LabelField(
                "Placement Rules",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Optional limits for where this specific spell may place " +
                "mines, areas, grenades, wires, or movement destinations.",
                MessageType.None);
            EditorGUILayout.PropertyField(
                placementRules.FindPropertyRelative("maximumDistance"));
            SerializedProperty lineOfSight =
                placementRules.FindPropertyRelative("requireLineOfSight");
            EditorGUILayout.PropertyField(lineOfSight);
            if (lineOfSight.boolValue)
            {
                EditorGUILayout.PropertyField(
                    placementRules.FindPropertyRelative("lineOfSightMask"));
                EditorGUILayout.PropertyField(
                    placementRules.FindPropertyRelative("lineOfSightRadius"));
            }
            EditorGUILayout.Space();
        }

        private void DrawAIGuidance()
        {
            aiAffordance.isExpanded = EditorGUILayout.Foldout(
                aiAffordance.isExpanded,
                new GUIContent(
                    "Enemy AI Guidance",
                    "Optional designer hints for when enemies should use this spell and how opponents may react to it."),
                toggleOnLabelClick: true);
            if (!aiAffordance.isExpanded)
            {
                EditorGUILayout.Space();
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                SerializedProperty usable =
                    aiAffordance.FindPropertyRelative("usableByAI");
                EditorGUILayout.PropertyField(usable);
                if (!usable.boolValue)
                {
                    EditorGUILayout.HelpBox(
                        "Enemies will not equip or cast this spell. Player use is unchanged.",
                        MessageType.None);
                    return;
                }

                EditorGUILayout.LabelField(
                    "When should the AI use it?",
                    EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(
                    aiAffordance.FindPropertyRelative("intents"));
                EditorGUILayout.PropertyField(
                    aiAffordance.FindPropertyRelative("targetPreference"));
                EditorGUILayout.PropertyField(
                    aiAffordance.FindPropertyRelative(
                        "preferredMinimumRange"));
                EditorGUILayout.PropertyField(
                    aiAffordance.FindPropertyRelative(
                        "preferredMaximumRange"));
                EditorGUILayout.PropertyField(
                    aiAffordance.FindPropertyRelative(
                        "minimumUsefulTargets"));
                EditorGUILayout.PropertyField(
                    aiAffordance.FindPropertyRelative("baseUtility"));
                EditorGUILayout.PropertyField(
                    aiAffordance.FindPropertyRelative("commitmentRisk"));

                SpellAIIntent intents = (SpellAIIntent)
                    aiAffordance.FindPropertyRelative("intents").intValue;
                if ((intents & (SpellAIIntent.Execute |
                                SpellAIIntent.Escape)) != 0)
                {
                    EditorGUILayout.PropertyField(
                        aiAffordance.FindPropertyRelative("healthThreshold"));
                }

                EditorGUILayout.Space(3f);
                EditorGUILayout.LabelField(
                    "Combo planning",
                    EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(
                    aiAffordance.FindPropertyRelative("producesComboTags"),
                    includeChildren: true);
                EditorGUILayout.PropertyField(
                    aiAffordance.FindPropertyRelative("consumesComboTags"),
                    includeChildren: true);

                EditorGUILayout.Space(3f);
                EditorGUILayout.LabelField(
                    "How should opponents read it?",
                    EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(
                    aiAffordance.FindPropertyRelative(
                        "suggestedReactions"));
                EditorGUILayout.PropertyField(
                    aiAffordance.FindPropertyRelative("dangerRadius"));
                EditorGUILayout.PropertyField(
                    aiAffordance.FindPropertyRelative("reactionUrgency"));
                EditorGUILayout.PropertyField(
                    aiAffordance.FindPropertyRelative("telegraphDuration"));
            }
            EditorGUILayout.Space();
        }

        private void DrawComposition()
        {
            EditorGUILayout.LabelField(
                "Composition",
                EditorStyles.boldLabel);

            if (targets.Length > 1)
            {
                EditorGUILayout.PropertyField(
                    deliverySlot,
                    includeChildren: true);
                EditorGUILayout.PropertyField(effectSlots, includeChildren: true);
                EditorGUILayout.PropertyField(
                    eventEffectRoutes,
                    includeChildren: true);
                EditorGUILayout.PropertyField(
                    reactiveEffectGroups,
                    includeChildren: true);
            }
            else
            {
                DrawDeliverySlot();
                EditorGUILayout.LabelField(
                    "Default Effects",
                    EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "These effects are applied at this delivery's normal " +
                    "targeting moment. Use Event Effect Recipes below for " +
                    "additional behavior at other moments.",
                    MessageType.None);
                DrawEffectSlots();
                DrawEventEffectRoutes();
                DrawReactiveEffectGroups();
            }
            EditorGUILayout.Space();
        }

        private void DrawDeliverySlot()
        {
            if (deliverySlot == null)
                return;

            SerializedProperty delivery =
                deliverySlot.FindPropertyRelative("delivery");
            SerializedProperty settings =
                deliverySlot.FindPropertyRelative("settings");
            var definition =
                delivery.objectReferenceValue as DeliveryDefinition;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    new GUIContent(
                        "Delivery",
                        definition != null
                            ? GetDeliveryTooltip(definition)
                            : "Choose how this spell travels, reaches targets, or creates an area."),
                    EditorStyles.boldLabel);

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(
                    delivery,
                    new GUIContent(
                        "Delivery Module",
                        "Choose how the spell reaches its destination or targets, such as a projectile, melee arc, point click, or lingering area."));
                if (EditorGUI.EndChangeCheck())
                {
                    definition =
                        delivery.objectReferenceValue as DeliveryDefinition;
                    settings.managedReferenceValue = definition != null
                        ? definition.CreateDefaultSettings()
                        : null;
                }

                if (definition == null)
                {
                    EditorGUILayout.HelpBox(
                        "Assign a delivery module to populate its settings.",
                        MessageType.Info);
                    return;
                }

                EnsureCompatibleSettings(definition, settings);
                if (definition.SettingsType == null)
                {
                    EditorGUILayout.HelpBox(
                        "This delivery currently uses its shared asset settings.",
                        MessageType.None);
                    return;
                }

                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField(
                    "Per-Spell Delivery Settings",
                    EditorStyles.boldLabel);
                DrawManagedReferenceChildren(settings);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(
                            new GUIContent(
                                "Reset to Defaults",
                                "Replace this spell's delivery settings with the reusable delivery asset's current default values."),
                            GUILayout.Width(140f)))
                    {
                        settings.managedReferenceValue =
                            definition.CreateDefaultSettings();
                    }
                }
            }

            EditorGUILayout.Space(4f);
        }

        private void DrawEffectSlots()
        {
            if (effectSlots == null)
                return;

            for (int i = 0; i < effectSlots.arraySize; i++)
                DrawEffectSlot(i);

            if (GUILayout.Button(
                    new GUIContent(
                        "+ Add Effect",
                        "Add an effect that this delivery applies at its normal effect moment."),
                    GUILayout.Height(24f)))
                ShowAddEffectMenu();
        }

        private void DrawEventEffectRoutes()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "Event Effect Recipes",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Optional WHEN → APPLY recipes for creative combinations. " +
                "Each recipe listens to a delivery moment, chooses who or " +
                "what receives it, and runs only the effects shown inside.",
                MessageType.None);

            if (eventEffectRoutes.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "No extra event-driven behavior. The spell still uses " +
                    "its Default Effects normally.",
                    MessageType.Info);
            }

            for (int i = 0; i < eventEffectRoutes.arraySize; i++)
                DrawEventEffectRoute(i);

            if (GUILayout.Button(
                    new GUIContent(
                        "+ Add Event Effect Recipe",
                        "Add behavior that runs at a specific moment reported by this spell's own delivery."),
                    GUILayout.Height(24f)))
            {
                AddEventEffectRoute();
            }
        }

        private void DrawEventEffectRoute(int routeIndex)
        {
            SerializedProperty route = eventEffectRoutes
                .GetArrayElementAtIndex(routeIndex);
            SerializedProperty enabled =
                route.FindPropertyRelative("enabled");
            SerializedProperty displayNameProperty =
                route.FindPropertyRelative("displayName");
            SerializedProperty trigger =
                route.FindPropertyRelative("trigger");
            SerializedProperty subjectRuleMode =
                route.FindPropertyRelative("subjectRuleMode");
            SerializedProperty customSubjectRules =
                route.FindPropertyRelative("customSubjectRules");
            SerializedProperty recipient =
                route.FindPropertyRelative("recipient");
            SerializedProperty routeEffects =
                route.FindPropertyRelative("effectSlots");
            string routeName = string.IsNullOrWhiteSpace(
                    displayNameProperty.stringValue)
                ? $"Event Effect Recipe {routeIndex + 1}"
                : displayNameProperty.stringValue;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    enabled.boolValue = GUILayout.Toggle(
                        enabled.boolValue,
                        new GUIContent(
                            string.Empty,
                            "Enable or disable this recipe without deleting its setup."),
                        GUILayout.Width(18f));
                    route.isExpanded = EditorGUILayout.Foldout(
                        route.isExpanded,
                        new GUIContent(
                            routeName,
                            "Expand or collapse this Event Effect Recipe."),
                        toggleOnLabelClick: true);
                    if (GUILayout.Button(
                            new GUIContent(
                                "×",
                                "Delete this Event Effect Recipe."),
                            GUILayout.Width(24f)))
                    {
                        eventEffectRoutes.DeleteArrayElementAtIndex(
                            routeIndex);
                        return;
                    }
                }

                if (!route.isExpanded)
                    return;

                EditorGUILayout.PropertyField(
                    displayNameProperty,
                    new GUIContent(
                        "Recipe Name",
                        "A short description of this behavior, such as Teleport on Impact or Refund AP on Miss."));

                EditorGUILayout.Space(3f);
                EditorGUILayout.LabelField(
                    "WHEN — What moment triggers this?",
                    EditorStyles.boldLabel);
                DrawSpellEventPopup(trigger);

                EditorGUILayout.Space(3f);
                EditorGUILayout.LabelField(
                    "ONLY IF — Which involved objects count?",
                    EditorStyles.boldLabel);
                DrawSubjectRuleModePopup(subjectRuleMode);
                if ((SpellEventSubjectRuleMode)
                        subjectRuleMode.enumValueIndex ==
                    SpellEventSubjectRuleMode.CustomRules)
                {
                    EditorGUILayout.PropertyField(
                        customSubjectRules,
                        new GUIContent(
                            "Custom Subject Rules",
                            "Rules applied only to the object involved in this recipe's event."),
                        includeChildren: true);
                }

                EditorGUILayout.Space(3f);
                EditorGUILayout.LabelField(
                    "APPLY TO — Who receives the effects?",
                    EditorStyles.boldLabel);
                DrawEventRecipientPopup(recipient);

                EditorGUILayout.Space(3f);
                EditorGUILayout.LabelField(
                    "EFFECTS — What happens?",
                    EditorStyles.boldLabel);
                if (routeEffects.arraySize == 0)
                {
                    EditorGUILayout.HelpBox(
                        "Add at least one effect to complete this recipe.",
                        MessageType.Warning);
                }

                for (int effectIndex = 0;
                     effectIndex < routeEffects.arraySize;
                     effectIndex++)
                {
                    DrawEffectSlot(routeEffects, effectIndex);
                }

                if (GUILayout.Button(
                        new GUIContent(
                            "+ Add Effect to Recipe",
                            "Choose an effect to run whenever this recipe's event and object rules match.")))
                {
                    int capturedRouteIndex = routeIndex;
                    ShowEffectDefinitionMenu(
                        definition => AddEventRouteEffect(
                            capturedRouteIndex,
                            definition));
                }

                DrawEventRouteWarnings(
                    (SpellEventType)trigger.enumValueIndex,
                    (SpellEventSubjectRuleMode)
                        subjectRuleMode.enumValueIndex,
                    (SpellEventRecipient)recipient.enumValueIndex,
                    routeEffects);

                EditorGUILayout.HelpBox(
                    BuildEventRouteSummary(
                        trigger,
                        subjectRuleMode,
                        recipient,
                        routeEffects),
                    MessageType.None);
            }
        }

        private void AddEventEffectRoute()
        {
            serializedObject.Update();
            int index = eventEffectRoutes.arraySize;
            eventEffectRoutes.arraySize++;
            SerializedProperty route = eventEffectRoutes
                .GetArrayElementAtIndex(index);
            route.FindPropertyRelative("enabled").boolValue = true;
            route.FindPropertyRelative("displayName").stringValue =
                $"Event Effect Recipe {index + 1}";
            route.FindPropertyRelative("stableId").stringValue =
                System.Guid.NewGuid().ToString("N");
            route.FindPropertyRelative("trigger").enumValueIndex =
                (int)SpellEventType.TargetHit;
            route.FindPropertyRelative("subjectRuleMode").enumValueIndex =
                (int)SpellEventSubjectRuleMode.UseSpellTargetRules;
            route.FindPropertyRelative("recipient").enumValueIndex =
                (int)SpellEventRecipient.EventSubject;
            SerializedProperty customRules =
                route.FindPropertyRelative("customSubjectRules");
            customRules.FindPropertyRelative("relationship").enumValueIndex =
                (int)TargetRelationship.Enemies;
            customRules.FindPropertyRelative("useLayerMask").boolValue = false;
            customRules.FindPropertyRelative("allowedLayers").intValue = 0;
            customRules.FindPropertyRelative("requireSpellTarget")
                .boolValue = false;
            route.FindPropertyRelative("effectSlots").arraySize = 0;
            route.isExpanded = true;
            serializedObject.ApplyModifiedProperties();
        }

        private void AddEventRouteEffect(
            int routeIndex,
            EffectDefinition definition)
        {
            serializedObject.Update();
            if (routeIndex < 0 || routeIndex >= eventEffectRoutes.arraySize)
                return;

            SerializedProperty slots = eventEffectRoutes
                .GetArrayElementAtIndex(routeIndex)
                .FindPropertyRelative("effectSlots");
            int index = slots.arraySize;
            slots.arraySize++;
            SerializedProperty slot = slots.GetArrayElementAtIndex(index);
            slot.FindPropertyRelative("effect").objectReferenceValue =
                definition;
            slot.FindPropertyRelative("settings").managedReferenceValue =
                definition != null
                    ? definition.CreateDefaultSettings()
                    : null;
            slot.isExpanded = true;
            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawSpellEventPopup(SerializedProperty trigger)
        {
            string[] labels =
            {
                "Cast Begins",
                "Delivery Begins",
                "Chosen Point Is Reached",
                "Target Is Hit",
                "Blocking Collider Is Hit",
                "Delivery Stops",
                "Area Is Created",
                "Area Applies a Pulse",
                "Object Enters Area",
                "Object Leaves Area",
                "Delivery Expires",
                "Started by a Delivery Reaction",
                "Delivery Becomes Armed",
                "Object Crosses the Trip Wire",
                "Object Enters Mine Proximity",
                "Timer or Fuse Expires",
                "Delivery Bounces",
                "Grenade Sticks",
                "Projectile Is Deflected",
                "Delivery Detonates"
            };
            int selected = Mathf.Clamp(
                trigger.enumValueIndex - 1,
                0,
                labels.Length - 1);
            selected = EditorGUILayout.Popup(
                new GUIContent(
                    "Event",
                    "Choose the moment from this spell's own delivery that starts this recipe."),
                selected,
                labels);
            trigger.enumValueIndex = selected + 1;
        }

        private static void DrawSubjectRuleModePopup(
            SerializedProperty subjectRuleMode)
        {
            string[] labels =
            {
                "No Restrictions",
                "Require an Involved Object",
                "Use Spell Target Rules",
                "Use Custom Rules"
            };
            subjectRuleMode.enumValueIndex = EditorGUILayout.Popup(
                new GUIContent(
                    "Involved Object",
                    "The object that caused the event, such as the enemy or wall hit by a projectile."),
                subjectRuleMode.enumValueIndex,
                labels);
        }

        private static void DrawEventRecipientPopup(
            SerializedProperty recipient)
        {
            string[] labels =
            {
                "The Involved Object",
                "The Spell Caster",
                "The Originally Selected Target",
                "The World Point Only"
            };
            recipient.enumValueIndex = EditorGUILayout.Popup(
                new GUIContent(
                    "Recipient",
                    "Choose who receives object-based effects, or choose World Point for effects that need only a location."),
                recipient.enumValueIndex,
                labels);
        }

        private void DrawEventRouteWarnings(
            SpellEventType eventType,
            SpellEventSubjectRuleMode subjectMode,
            SpellEventRecipient recipientMode,
            SerializedProperty routeEffects)
        {
            SpellDefinition spell = target as SpellDefinition;
            DeliveryDefinition delivery = spell != null
                ? spell.Delivery
                : null;
            if (delivery != null && !DeliveryUsuallyReportsEvent(
                    delivery,
                    eventType))
            {
                EditorGUILayout.HelpBox(
                    $"{delivery.DisplayName} does not currently report the " +
                    $"'{GetSpellEventLabel(eventType)}' event.",
                    MessageType.Warning);
            }

            bool usuallyHasSubject = EventUsuallyHasSubject(eventType);
            if (!usuallyHasSubject &&
                (subjectMode ==
                     SpellEventSubjectRuleMode.RequireEventSubject ||
                 subjectMode ==
                     SpellEventSubjectRuleMode.UseSpellTargetRules ||
                 subjectMode ==
                     SpellEventSubjectRuleMode.CustomRules))
            {
                EditorGUILayout.HelpBox(
                    "This event usually has no involved object, so these " +
                    "subject rules may prevent the recipe from running.",
                    MessageType.Warning);
            }

            if (!usuallyHasSubject &&
                recipientMode == SpellEventRecipient.EventSubject)
            {
                EditorGUILayout.HelpBox(
                    "This event usually has no involved object. Choose the " +
                    "caster, selected target, or world point as recipient.",
                    MessageType.Warning);
            }

            if (recipientMode == SpellEventRecipient.WorldPoint)
            {
                for (int i = 0; i < routeEffects.arraySize; i++)
                {
                    SerializedProperty slot =
                        routeEffects.GetArrayElementAtIndex(i);
                    var effect = slot.FindPropertyRelative("effect")
                        .objectReferenceValue as EffectDefinition;
                    object settings = slot.FindPropertyRelative("settings")
                        .managedReferenceValue;
                    if (effect != null &&
                        !effect.CanApplyWithoutRecipient(
                            settings as SpellEffectSettings))
                    {
                        EditorGUILayout.HelpBox(
                            $"{effect.DisplayName} requires an object " +
                            "recipient. Choose another recipient or use an " +
                            "effect that supports world points.",
                            MessageType.Warning);
                    }
                }
            }
        }

        private static string BuildEventRouteSummary(
            SerializedProperty trigger,
            SerializedProperty subjectRuleMode,
            SerializedProperty recipient,
            SerializedProperty effects)
        {
            string effectText;
            if (effects.arraySize == 0)
            {
                effectText = "no effects yet";
            }
            else
            {
                var names = new List<string>();
                for (int i = 0; i < effects.arraySize; i++)
                {
                    var definition = effects.GetArrayElementAtIndex(i)
                        .FindPropertyRelative("effect")
                        .objectReferenceValue as EffectDefinition;
                    names.Add(definition != null
                        ? definition.DisplayName
                        : "an unassigned effect");
                }
                effectText = string.Join(", then ", names);
            }

            return $"When {GetSpellEventLabel((SpellEventType)trigger.enumValueIndex).ToLowerInvariant()}, " +
                   $"apply {effectText} to " +
                   $"{GetRecipientLabel((SpellEventRecipient)recipient.enumValueIndex).ToLowerInvariant()}. " +
                   $"Involved object rule: " +
                   $"{GetSubjectRuleLabel((SpellEventSubjectRuleMode)subjectRuleMode.enumValueIndex).ToLowerInvariant()}.";
        }

        private static string GetSpellEventLabel(SpellEventType eventType)
        {
            switch (eventType)
            {
                case SpellEventType.CastStarted: return "the cast begins";
                case SpellEventType.DeliveryStarted: return "the delivery begins";
                case SpellEventType.PointReached: return "the chosen point is reached";
                case SpellEventType.TargetHit: return "a target is hit";
                case SpellEventType.BlockingHit: return "a blocking collider is hit";
                case SpellEventType.DeliveryStopped: return "the delivery stops";
                case SpellEventType.AreaCreated: return "the area is created";
                case SpellEventType.AreaPulse: return "the area applies a pulse";
                case SpellEventType.TargetEnteredArea: return "an object enters the area";
                case SpellEventType.TargetExitedArea: return "an object leaves the area";
                case SpellEventType.DeliveryExpired: return "the delivery expires";
                case SpellEventType.ManualReaction: return "a delivery reaction starts it";
                case SpellEventType.Armed: return "the delivery becomes armed";
                case SpellEventType.TargetCrossed: return "an object crosses the trip wire";
                case SpellEventType.ProximityTriggered: return "an object enters mine proximity";
                case SpellEventType.TimerExpired: return "the timer or fuse expires";
                case SpellEventType.Bounced: return "the delivery bounces";
                case SpellEventType.Stuck: return "the grenade sticks";
                case SpellEventType.Deflected: return "the projectile is deflected";
                case SpellEventType.Detonated: return "the delivery detonates";
                default: return "an event occurs";
            }
        }

        private static string GetRecipientLabel(
            SpellEventRecipient recipient)
        {
            switch (recipient)
            {
                case SpellEventRecipient.Caster: return "the spell caster";
                case SpellEventRecipient.SelectedTarget:
                    return "the originally selected target";
                case SpellEventRecipient.WorldPoint:
                    return "the event's world point";
                default: return "the involved object";
            }
        }

        private static string GetSubjectRuleLabel(
            SpellEventSubjectRuleMode mode)
        {
            switch (mode)
            {
                case SpellEventSubjectRuleMode.RequireEventSubject:
                    return "an involved object is required";
                case SpellEventSubjectRuleMode.UseSpellTargetRules:
                    return "use the spell's Target Rules";
                case SpellEventSubjectRuleMode.CustomRules:
                    return "use this recipe's Custom Rules";
                default: return "no restrictions";
            }
        }

        private static bool EventUsuallyHasSubject(SpellEventType eventType)
        {
            return eventType == SpellEventType.TargetHit ||
                   eventType == SpellEventType.BlockingHit ||
                   eventType == SpellEventType.TargetEnteredArea ||
                   eventType == SpellEventType.TargetExitedArea ||
                   eventType == SpellEventType.TargetCrossed ||
                   eventType == SpellEventType.ProximityTriggered ||
                   eventType == SpellEventType.Stuck ||
                   eventType == SpellEventType.Deflected ||
                   eventType == SpellEventType.ManualReaction;
        }

        private static bool DeliveryUsuallyReportsEvent(
            DeliveryDefinition delivery,
            SpellEventType eventType)
        {
            return SpellEventSupport.DeliveryReports(delivery, eventType);
        }

        private void DrawReactiveEffectGroups()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(
                "Reactive Effect Groups",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Optional named effect sets that reactions can enable or " +
                "disable. Active groups are applied by a Lingering Area to " +
                "current and future valid occupants.",
                MessageType.None);

            if (reactiveEffectGroups.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "This spell has no reactive effect groups.",
                    MessageType.Info);
            }

            for (int i = 0; i < reactiveEffectGroups.arraySize; i++)
                DrawReactiveEffectGroup(i);

            if (GUILayout.Button(
                    new GUIContent(
                        "+ Add Reactive Effect Group",
                        "Add a named set of area effects that a Reaction can enable or disable."),
                    GUILayout.Height(24f)))
            {
                AddReactiveEffectGroup();
            }
        }

        private void DrawReactiveEffectGroup(int groupIndex)
        {
            SerializedProperty group = reactiveEffectGroups
                .GetArrayElementAtIndex(groupIndex);
            SerializedProperty displayNameProperty =
                group.FindPropertyRelative("displayName");
            SerializedProperty startsActive =
                group.FindPropertyRelative("startsActive");
            SerializedProperty useSpellTargetRules =
                group.FindPropertyRelative("useSpellTargetRules");
            SerializedProperty groupTargetFilter =
                group.FindPropertyRelative("targetFilter");
            SerializedProperty groupEffects =
                group.FindPropertyRelative("effectSlots");
            string groupName = string.IsNullOrWhiteSpace(
                    displayNameProperty.stringValue)
                ? $"Reactive Effect Group {groupIndex + 1}"
                : displayNameProperty.stringValue;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    group.isExpanded = EditorGUILayout.Foldout(
                        group.isExpanded,
                        new GUIContent(
                            groupName,
                            "Expand or collapse this Reactive Effect Group."),
                        toggleOnLabelClick: true);
                    if (GUILayout.Button(
                            new GUIContent(
                                "×",
                                "Delete this Reactive Effect Group."),
                            GUILayout.Width(24f)))
                    {
                        reactiveEffectGroups.DeleteArrayElementAtIndex(
                            groupIndex);
                        return;
                    }
                }

                if (!group.isExpanded)
                    return;

                EditorGUILayout.PropertyField(
                    displayNameProperty,
                    new GUIContent(
                        "Group Name",
                        "A short name for this alternate behavior, such as Ignited Damage or Frozen Slow."));
                EditorGUILayout.PropertyField(
                    startsActive,
                    new GUIContent(
                        "Starts Active",
                        "Enable these effects as soon as the area appears. Disable this when a Reaction should unlock them later."));
                EditorGUILayout.PropertyField(
                    useSpellTargetRules,
                    new GUIContent(
                        "Use Spell Target Rules",
                        "Use the spell's main Target Rules. Disable it to define separate targets for this group."));
                if (!useSpellTargetRules.boolValue)
                {
                    EditorGUILayout.PropertyField(
                        groupTargetFilter,
                        new GUIContent(
                            "Group Target Rules",
                            "Independent rules deciding which occupants can receive this group's effects."),
                        includeChildren: true);
                }

                EditorGUILayout.Space(3f);
                EditorGUILayout.LabelField(
                    "Effects in This Group",
                    EditorStyles.boldLabel);
                if (groupEffects.arraySize == 0)
                {
                    EditorGUILayout.HelpBox(
                        "This group has no effects to apply.",
                        MessageType.Warning);
                }

                for (int effectIndex = 0;
                     effectIndex < groupEffects.arraySize;
                     effectIndex++)
                {
                    DrawEffectSlot(groupEffects, effectIndex);
                }

                if (GUILayout.Button(
                        new GUIContent(
                            "+ Add Effect to Group",
                            "Choose an effect that is applied while this group is active.")))
                {
                    int capturedGroupIndex = groupIndex;
                    ShowEffectDefinitionMenu(
                        definition => AddReactiveGroupEffect(
                            capturedGroupIndex,
                            definition));
                }
            }
        }

        private void AddReactiveEffectGroup()
        {
            serializedObject.Update();
            int index = reactiveEffectGroups.arraySize;
            reactiveEffectGroups.arraySize++;
            SerializedProperty group = reactiveEffectGroups
                .GetArrayElementAtIndex(index);
            group.FindPropertyRelative("displayName").stringValue =
                $"Reactive Effect Group {index + 1}";
            group.FindPropertyRelative("stableId").stringValue =
                System.Guid.NewGuid().ToString("N");
            group.FindPropertyRelative("startsActive").boolValue = false;
            group.FindPropertyRelative("useSpellTargetRules").boolValue = true;
            SerializedProperty groupTargetFilter =
                group.FindPropertyRelative("targetFilter");
            groupTargetFilter.FindPropertyRelative("relationship")
                .enumValueIndex = (int)TargetRelationship.Enemies;
            groupTargetFilter.FindPropertyRelative("useLayerMask")
                .boolValue = false;
            groupTargetFilter.FindPropertyRelative("allowedLayers")
                .intValue = 0;
            groupTargetFilter.FindPropertyRelative("requireSpellTarget")
                .boolValue = false;
            group.FindPropertyRelative("effectSlots").arraySize = 0;
            group.isExpanded = true;
            serializedObject.ApplyModifiedProperties();
        }

        private void AddReactiveGroupEffect(
            int groupIndex,
            EffectDefinition definition)
        {
            serializedObject.Update();
            if (groupIndex < 0 ||
                groupIndex >= reactiveEffectGroups.arraySize)
            {
                return;
            }

            SerializedProperty slots = reactiveEffectGroups
                .GetArrayElementAtIndex(groupIndex)
                .FindPropertyRelative("effectSlots");
            int index = slots.arraySize;
            slots.arraySize++;
            SerializedProperty slot = slots.GetArrayElementAtIndex(index);
            slot.FindPropertyRelative("effect").objectReferenceValue =
                definition;
            slot.FindPropertyRelative("settings").managedReferenceValue =
                definition != null
                    ? definition.CreateDefaultSettings()
                    : null;
            slot.isExpanded = true;
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawReactions()
        {
            EditorGUILayout.LabelField(
                "Delivery Reactions — Other Spells Affect This Delivery",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Use this only when another V2 delivery touching this " +
                "persistent delivery should cause something. Event Effect " +
                "Recipes above handle moments produced by this spell's own " +
                "delivery.",
                MessageType.None);

            if (targets.Length > 1)
            {
                EditorGUILayout.PropertyField(
                    reactionSlots,
                    includeChildren: true);
                EditorGUILayout.Space();
                return;
            }

            SpellDefinition spell = target as SpellDefinition;
            if (reactionSlots.arraySize > 0 &&
                spell != null &&
                !(spell.Delivery is LingeringAreaDeliveryDefinition))
            {
                EditorGUILayout.HelpBox(
                    "Reactions currently execute only on the Lingering Area " +
                    "delivery. This spell's reactions will not run with its " +
                    "current delivery module.",
                    MessageType.Warning);
            }

            if (reactionSlots.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "This spell does not react to other deliveries.",
                    MessageType.Info);
            }
            else if (reactionSlots.arraySize > 1)
            {
                EditorGUILayout.HelpBox(
                    "Each reaction evaluates independently. One contact can " +
                    "trigger more than one matching reaction.",
                    MessageType.None);
            }

            for (int i = 0; i < reactionSlots.arraySize; i++)
                DrawReactionSlot(i);

            if (GUILayout.Button(
                    new GUIContent(
                        "+ Add Reaction",
                        "Add behavior caused by another V2 delivery touching this persistent delivery."),
                    GUILayout.Height(24f)))
            {
                serializedObject.Update();
                int index = reactionSlots.arraySize;
                reactionSlots.arraySize++;
                SerializedProperty slot =
                    reactionSlots.GetArrayElementAtIndex(index);
                slot.FindPropertyRelative("enabled").boolValue = true;
                SerializedProperty filter =
                    slot.FindPropertyRelative("filter");
                filter.FindPropertyRelative("matchMode").enumValueIndex =
                    (int)InteractionFilterMatchMode.All;
                filter.FindPropertyRelative("conditions").arraySize = 0;
                SerializedProperty responses =
                    slot.FindPropertyRelative("responses");
                responses.arraySize = 0;
                slot.FindPropertyRelative("triggerPolicy").enumValueIndex =
                    (int)InteractionTriggerPolicy.OncePerSourceDelivery;
                slot.FindPropertyRelative("cooldown").floatValue = 0f;
                slot.isExpanded = true;
                serializedObject.ApplyModifiedProperties();
            }

            EditorGUILayout.Space();
        }

        private void DrawReactionSlot(int index)
        {
            SerializedProperty slot =
                reactionSlots.GetArrayElementAtIndex(index);
            SerializedProperty enabled = slot.FindPropertyRelative("enabled");
            SerializedProperty filter = slot.FindPropertyRelative("filter");
            SerializedProperty responses = slot.FindPropertyRelative("responses");
            SerializedProperty policy =
                slot.FindPropertyRelative("triggerPolicy");
            SerializedProperty cooldown =
                slot.FindPropertyRelative("cooldown");
            string title = GetReactionTitle(
                index,
                filter,
                responses);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    enabled.boolValue = GUILayout.Toggle(
                        enabled.boolValue,
                        new GUIContent(
                            string.Empty,
                            "Enable or disable this reaction without deleting it."),
                        GUILayout.Width(18f));
                    slot.isExpanded = EditorGUILayout.Foldout(
                        slot.isExpanded,
                        new GUIContent(
                            title,
                            "Expand or collapse this Delivery Reaction."),
                        toggleOnLabelClick: true);
                    if (GUILayout.Button(
                            new GUIContent(
                                "×",
                                "Delete this Delivery Reaction."),
                            GUILayout.Width(24f)))
                    {
                        reactionSlots.DeleteArrayElementAtIndex(index);
                        return;
                    }
                }

                if (!slot.isExpanded)
                    return;

                DrawReactionFilter(index, filter);
                DrawReactionFrequency(policy, cooldown);

                EditorGUILayout.Space(5f);
                EditorGUILayout.LabelField(
                    "3. THEN — What should happen?",
                    EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Actions run from top to bottom.",
                    EditorStyles.miniLabel);
                if (responses.arraySize == 0)
                {
                    EditorGUILayout.HelpBox(
                        "This reaction can match a contact but performs no actions.",
                        MessageType.Warning);
                }

                for (int responseIndex = 0;
                     responseIndex < responses.arraySize;
                     responseIndex++)
                {
                    SerializedProperty response =
                        responses.GetArrayElementAtIndex(responseIndex);
                    var responseValue = response.managedReferenceValue as
                        DeliveryInteractionResponse;
                    string responseName = responseValue != null
                        ? GetResponseName(response)
                        : $"Response {responseIndex + 1}";

                    using (new EditorGUILayout.VerticalScope(
                               EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField(
                                $"{responseIndex + 1}. {responseName}",
                                EditorStyles.boldLabel);
                            GUI.enabled = responseIndex > 0;
                            if (GUILayout.Button(
                                    new GUIContent(
                                        "▲",
                                        "Move this action earlier. Actions run from top to bottom."),
                                    GUILayout.Width(24f)))
                            {
                                responses.MoveArrayElement(
                                    responseIndex,
                                    responseIndex - 1);
                                GUI.enabled = true;
                                return;
                            }

                            GUI.enabled = responseIndex <
                                          responses.arraySize - 1;
                            if (GUILayout.Button(
                                    new GUIContent(
                                        "▼",
                                        "Move this action later. Actions run from top to bottom."),
                                    GUILayout.Width(24f)))
                            {
                                responses.MoveArrayElement(
                                    responseIndex,
                                    responseIndex + 1);
                                GUI.enabled = true;
                                return;
                            }

                            GUI.enabled = true;
                            if (GUILayout.Button(
                                    new GUIContent(
                                        "×",
                                        "Remove this action from the reaction."),
                                    GUILayout.Width(24f)))
                            {
                                responses.DeleteArrayElementAtIndex(
                                    responseIndex);
                                return;
                            }
                        }

                        if (response.managedReferenceValue != null)
                            DrawResponseFields(response);
                    }
                }

                if (GUILayout.Button(
                        new GUIContent(
                            "+ Add Action",
                            "Add something this delivery does after the trigger rules match.")))
                    ShowResponseMenu(index);

                DrawReactionWarnings(policy, cooldown, responses);
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox(
                    "What this reaction does:\n" +
                    BuildReactionSummary(filter, policy, cooldown, responses),
                    MessageType.Info);
            }
        }

        private string GetReactionTitle(
            int reactionIndex,
            SerializedProperty filter,
            SerializedProperty responses)
        {
            SerializedProperty conditions = filter != null
                ? filter.FindPropertyRelative("conditions")
                : null;
            string source = conditions == null || conditions.arraySize == 0
                ? "Any Delivery"
                : conditions.arraySize == 1
                    ? BuildConditionSummary(
                        conditions.GetArrayElementAtIndex(0),
                        compact: true)
                    : $"{conditions.arraySize} Trigger Rules";

            if (responses == null || responses.arraySize == 0)
                return $"Reaction {reactionIndex + 1} — {source} → No Actions";

            var actions = new List<string>();
            for (int i = 0; i < responses.arraySize && i < 3; i++)
            {
                actions.Add(GetResponseName(
                    responses.GetArrayElementAtIndex(i)));
            }

            string actionSummary = string.Join(" → ", actions);
            if (responses.arraySize > 3)
                actionSummary += $" + {responses.arraySize - 3}";
            return $"Reaction {reactionIndex + 1} — {source} → {actionSummary}";
        }

        private void DrawReactionFilter(
            int reactionIndex,
            SerializedProperty filter)
        {
            EditorGUILayout.Space(5f);
            EditorGUILayout.LabelField(
                "1. WHEN — What can trigger this?",
                EditorStyles.boldLabel);
            SerializedProperty matchMode =
                filter.FindPropertyRelative("matchMode");
            SerializedProperty conditions =
                filter.FindPropertyRelative("conditions");

            if (conditions.arraySize > 1)
            {
                string[] matchLabels = { "All rules", "Any rule" };
                matchMode.enumValueIndex = EditorGUILayout.Popup(
                    new GUIContent(
                        "Require",
                        "Choose whether every trigger rule must match or whether any single rule is enough."),
                    matchMode.enumValueIndex,
                    matchLabels);
                EditorGUILayout.LabelField(
                    matchMode.enumValueIndex ==
                    (int)InteractionFilterMatchMode.All
                        ? "Every rule below must match."
                        : "At least one rule below must match.",
                    EditorStyles.miniLabel);
            }

            if (conditions.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "No trigger rules: every V2 delivery can trigger this reaction.",
                    MessageType.Info);
            }

            for (int i = 0; i < conditions.arraySize; i++)
            {
                SerializedProperty condition =
                    conditions.GetArrayElementAtIndex(i);
                var value = condition.managedReferenceValue as
                    DeliveryInteractionCondition;
                string name = value != null
                    ? GetConditionName(value)
                    : $"Condition {i + 1}";

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(name, EditorStyles.boldLabel);
                        if (GUILayout.Button(
                                new GUIContent(
                                    "×",
                                    "Remove this trigger rule."),
                                GUILayout.Width(24f)))
                        {
                            conditions.DeleteArrayElementAtIndex(i);
                            break;
                        }
                    }

                    if (condition.managedReferenceValue != null)
                    {
                        DrawConditionFields(condition, value);
                        if (ConditionHasMissingValue(condition, value))
                        {
                            EditorGUILayout.HelpBox(
                                "Assign a value or this rule cannot match.",
                                MessageType.Warning);
                        }
                    }
                }
            }

            if (GUILayout.Button(
                    new GUIContent(
                        "+ Add Trigger Rule",
                        "Add a condition describing which incoming deliveries are allowed to trigger this reaction.")))
                ShowConditionMenu(reactionIndex);
        }

        private static void DrawReactionFrequency(
            SerializedProperty policy,
            SerializedProperty cooldown)
        {
            EditorGUILayout.Space(5f);
            EditorGUILayout.LabelField(
                "2. HOW OFTEN — Trigger behavior",
                EditorStyles.boldLabel);
            string[] labels =
            {
                "Every matching contact",
                "Once per individual delivery",
                "Only once"
            };
            policy.enumValueIndex = EditorGUILayout.Popup(
                new GUIContent(
                    "Trigger",
                    "Choose whether this reaction repeats, runs once per incoming delivery, or runs only once for this area instance."),
                policy.enumValueIndex,
                labels);

            switch ((InteractionTriggerPolicy)policy.enumValueIndex)
            {
                case InteractionTriggerPolicy.EveryContact:
                    EditorGUILayout.LabelField(
                        "Runs whenever a matching contact is reported.",
                        EditorStyles.miniLabel);
                    break;
                case InteractionTriggerPolicy.OncePerSourceDelivery:
                    EditorGUILayout.LabelField(
                        "Each projectile, slash, or delivery instance can trigger it once.",
                        EditorStyles.miniLabel);
                    break;
                default:
                    EditorGUILayout.LabelField(
                        "The first matching contact permanently completes this reaction.",
                        EditorStyles.miniLabel);
                    break;
            }

            if ((InteractionTriggerPolicy)policy.enumValueIndex !=
                InteractionTriggerPolicy.OnceTotal)
            {
                EditorGUILayout.PropertyField(
                    cooldown,
                    new GUIContent(
                        "Minimum Delay",
                        "Unscaled seconds before this reaction may run again."));
            }
        }

        private void ShowConditionMenu(int reactionIndex)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(
                    "Who Cast It/Relationship",
                    "Compare the incoming caster to this delivery's caster, such as Enemy or Ally."), false,
                () => AddCondition(reactionIndex,
                    new InteractionRelationshipCondition()));
            menu.AddItem(new GUIContent(
                    "Who Cast It/Caster Team",
                    "Require an absolute team such as Player or Enemy."), false,
                () => AddCondition(reactionIndex,
                    new InteractionCasterTeamCondition()));
            menu.AddItem(new GUIContent(
                    "Which Spell/Exact Spell",
                    "Only one exact Spell Definition can match."), false,
                () => AddCondition(reactionIndex,
                    new InteractionSpellCondition()));
            menu.AddItem(new GUIContent(
                    "Which Spell/Spell Category",
                    "Match the incoming spell's Category text."), false,
                () => AddCondition(reactionIndex,
                    new InteractionSpellCategoryCondition()));
            menu.AddItem(new GUIContent(
                    "How It Arrived/Contact Phase",
                    "Choose contact moments such as impact, enter, stay, or exit."), false,
                () => AddCondition(reactionIndex,
                    new InteractionContactPhaseCondition()));
            menu.AddItem(new GUIContent(
                    "How It Arrived/Delivery Module",
                    "Require a delivery type such as Projectile or Melee Arc."), false,
                () => AddCondition(reactionIndex,
                    new InteractionDeliveryCondition()));
            menu.AddItem(new GUIContent(
                    "What It Carries/Effect Module",
                    "The incoming spell must contain the selected reusable effect."), false,
                () => AddCondition(reactionIndex,
                    new InteractionEffectCondition()));
            menu.AddItem(new GUIContent(
                    "What It Carries/Damage Type",
                    "The incoming spell must contain an effect configured with this Damage Type."), false,
                () => AddCondition(reactionIndex,
                    new InteractionDamageTypeCondition()));
            menu.ShowAsContext();
        }

        private void AddCondition(
            int reactionIndex,
            DeliveryInteractionCondition condition)
        {
            serializedObject.Update();
            SerializedProperty conditions = reactionSlots
                .GetArrayElementAtIndex(reactionIndex)
                .FindPropertyRelative("filter")
                .FindPropertyRelative("conditions");
            int index = conditions.arraySize;
            conditions.arraySize++;
            conditions.GetArrayElementAtIndex(index).managedReferenceValue =
                condition;
            serializedObject.ApplyModifiedProperties();
        }

        private void ShowResponseMenu(int reactionIndex)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(
                    "Change Active State",
                    "Turn this persistent delivery's normal effect application on or off."), false,
                () => AddResponse(
                    reactionIndex,
                    new ActivateDeliveryResponse()));
            menu.AddItem(new GUIContent(
                    "Apply Effects to Occupants",
                    "Immediately apply this spell's Default Effects to valid objects currently inside."), false,
                () => AddResponse(
                    reactionIndex,
                    new PulseEffectsResponse()));
            menu.AddItem(
                new GUIContent(
                    "Enable or Disable Reactive Effect Group",
                    "Change the active state of one named Reactive Effect Group."),
                false,
                () => AddResponse(
                    reactionIndex,
                    new SetReactiveEffectGroupActiveResponse()));
            menu.AddItem(
                new GUIContent(
                    "Run Event Effect Recipe",
                    "Run a recipe whose WHEN event is Started by a Delivery Reaction."),
                false,
                () => AddResponse(
                    reactionIndex,
                    new RunEventEffectRouteResponse()));
            menu.AddItem(new GUIContent(
                    "Destroy This Delivery",
                    "Remove this persistent delivery after earlier actions finish."), false,
                () => AddResponse(
                    reactionIndex,
                    new DestroyDeliveryResponse()));
            menu.ShowAsContext();
        }

        private void AddResponse(
            int reactionIndex,
            DeliveryInteractionResponse response)
        {
            serializedObject.Update();
            SerializedProperty responses = reactionSlots
                .GetArrayElementAtIndex(reactionIndex)
                .FindPropertyRelative("responses");
            int responseIndex = responses.arraySize;
            responses.arraySize++;
            responses.GetArrayElementAtIndex(responseIndex)
                .managedReferenceValue = response;
            serializedObject.ApplyModifiedProperties();
        }

        private static string GetConditionName(
            DeliveryInteractionCondition condition)
        {
            if (condition is InteractionRelationshipCondition)
                return "Who Cast It — Relationship";
            if (condition is InteractionCasterTeamCondition)
                return "Who Cast It — Caster Team";
            if (condition is InteractionSpellCondition)
                return "Which Spell — Exact Spell";
            if (condition is InteractionSpellCategoryCondition)
                return "Which Spell — Spell Category";
            if (condition is InteractionContactPhaseCondition)
                return "How It Arrived — Contact Phase";
            if (condition is InteractionDeliveryCondition)
                return "How It Arrived — Delivery Module";
            if (condition is InteractionEffectCondition)
                return "What It Carries — Effect Module";
            if (condition is InteractionDamageTypeCondition)
                return "What It Carries — Damage Type";
            return condition != null
                ? condition.DisplayName
                : "Trigger Rule";
        }

        private static void DrawConditionFields(
            SerializedProperty condition,
            DeliveryInteractionCondition value)
        {
            SerializedProperty inverted =
                condition.FindPropertyRelative("inverted");
            SerializedProperty valueProperty = null;
            string subject = "Value";
            string positiveComparison = "IS";
            string negativeComparison = "IS NOT";

            if (value is InteractionRelationshipCondition)
            {
                subject = "Source relationship";
                valueProperty = condition.FindPropertyRelative("relationship");
            }
            else if (value is InteractionCasterTeamCondition)
            {
                subject = "Caster team";
                valueProperty = condition.FindPropertyRelative("team");
            }
            else if (value is InteractionSpellCondition)
            {
                subject = "Source spell";
                valueProperty = condition.FindPropertyRelative("spell");
            }
            else if (value is InteractionSpellCategoryCondition)
            {
                subject = "Spell category";
                valueProperty = condition.FindPropertyRelative("category");
            }
            else if (value is InteractionContactPhaseCondition)
            {
                subject = "Contact phase";
                positiveComparison = "INCLUDES";
                negativeComparison = "EXCLUDES";
                valueProperty = condition.FindPropertyRelative(
                    "acceptedPhases");
            }
            else if (value is InteractionDeliveryCondition)
            {
                subject = "Delivery module";
                valueProperty = condition.FindPropertyRelative("delivery");
            }
            else if (value is InteractionEffectCondition)
            {
                subject = "Carries effect";
                valueProperty = condition.FindPropertyRelative("effect");
            }
            else if (value is InteractionDamageTypeCondition)
            {
                subject = "Carries damage type";
                valueProperty = condition.FindPropertyRelative("damageType");
            }

            if (inverted == null || valueProperty == null)
            {
                DrawManagedReferenceChildren(condition);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    new GUIContent(
                        subject,
                        GetConditionTooltip(value)),
                    GUILayout.MinWidth(118f));
                string[] comparisons =
                {
                    positiveComparison,
                    negativeComparison
                };
                int comparison = inverted.boolValue ? 1 : 0;
                comparison = EditorGUILayout.Popup(
                    comparison,
                    comparisons,
                    GUILayout.Width(78f));
                inverted.boolValue = comparison == 1;
                EditorGUILayout.PropertyField(
                    valueProperty,
                    GUIContent.none,
                    includeChildren: true);
            }
        }

        private static string GetConditionTooltip(
            DeliveryInteractionCondition value)
        {
            if (value is InteractionRelationshipCondition)
                return "Compare the incoming caster to this delivery's caster.";
            if (value is InteractionCasterTeamCondition)
                return "Require an absolute combat team for the incoming caster.";
            if (value is InteractionSpellCondition)
                return "Require one exact incoming Spell Definition.";
            if (value is InteractionSpellCategoryCondition)
                return "Compare the incoming spell's Category text.";
            if (value is InteractionContactPhaseCondition)
                return "Choose which contact moments count, such as impact, enter, stay, or exit.";
            if (value is InteractionDeliveryCondition)
                return "Require one delivery module, such as Projectile or Melee Arc.";
            if (value is InteractionEffectCondition)
                return "Require the incoming spell to carry this effect in any composition section.";
            if (value is InteractionDamageTypeCondition)
                return "Require the incoming spell to carry an effect configured with this Damage Type.";
            return "Value checked by this trigger rule.";
        }

        private static bool ConditionHasMissingValue(
            SerializedProperty condition,
            DeliveryInteractionCondition value)
        {
            if (value is InteractionSpellCondition)
                return condition.FindPropertyRelative("spell")
                    .objectReferenceValue == null;
            if (value is InteractionSpellCategoryCondition)
                return string.IsNullOrWhiteSpace(
                    condition.FindPropertyRelative("category").stringValue);
            if (value is InteractionDeliveryCondition)
                return condition.FindPropertyRelative("delivery")
                    .objectReferenceValue == null;
            if (value is InteractionEffectCondition)
                return condition.FindPropertyRelative("effect")
                    .objectReferenceValue == null;
            if (value is InteractionDamageTypeCondition)
                return condition.FindPropertyRelative("damageType")
                    .objectReferenceValue == null;
            if (value is InteractionContactPhaseCondition)
                return condition.FindPropertyRelative("acceptedPhases")
                    .intValue == 0;
            return false;
        }

        private string GetResponseName(SerializedProperty response)
        {
            object value = response?.managedReferenceValue;
            if (value is ActivateDeliveryResponse)
            {
                SerializedProperty active =
                    response.FindPropertyRelative("active");
                return active != null && !active.boolValue
                    ? "Deactivate This Delivery"
                    : "Activate This Delivery";
            }
            if (value is PulseEffectsResponse)
                return "Apply Effects to Occupants";
            if (value is SetReactiveEffectGroupActiveResponse)
            {
                bool active = response.FindPropertyRelative("active")
                    .boolValue;
                return active
                    ? "Enable Reactive Effect Group"
                    : "Disable Reactive Effect Group";
            }
            if (value is RunEventEffectRouteResponse)
                return "Run Event Effect Recipe";
            if (value is DestroyDeliveryResponse)
                return "Destroy This Delivery";
            return value is DeliveryInteractionResponse known
                ? known.DisplayName
                : "Unassigned Action";
        }

        private void DrawResponseFields(SerializedProperty response)
        {
            object value = response.managedReferenceValue;
            if (value is ActivateDeliveryResponse)
            {
                SerializedProperty active =
                    response.FindPropertyRelative("active");
                SerializedProperty pulse = response.FindPropertyRelative(
                    "pulseEffectsImmediately");
                string[] states = { "Active", "Inactive" };
                int state = active.boolValue ? 0 : 1;
                state = EditorGUILayout.Popup(
                    new GUIContent(
                        "Set Delivery",
                        "Active deliveries apply their normal effects. Inactive deliveries remain present for reactions but do not apply them."),
                    state,
                    states);
                active.boolValue = state == 0;
                if (active.boolValue)
                {
                    EditorGUILayout.PropertyField(
                        pulse,
                        new GUIContent(
                            "Apply Effects Immediately",
                            "Also pulse this spell's effects as part of activation."));
                }
                return;
            }

            if (value is PulseEffectsResponse)
            {
                EditorGUILayout.LabelField(
                    "Applies this spell's equipped effects to valid current occupants.",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }

            if (value is SetReactiveEffectGroupActiveResponse)
            {
                SerializedProperty groupId =
                    response.FindPropertyRelative("groupId");
                SerializedProperty active =
                    response.FindPropertyRelative("active");
                SerializedProperty applyImmediately =
                    response.FindPropertyRelative(
                        "applyToCurrentOccupantsImmediately");
                DrawReactiveEffectGroupSelector(groupId);

                string[] states = { "Active", "Inactive" };
                int state = active.boolValue ? 0 : 1;
                state = EditorGUILayout.Popup(
                    new GUIContent(
                        "Set Group",
                        "Enable or disable the selected Reactive Effect Group for this delivery instance."),
                    state,
                    states);
                active.boolValue = state == 0;
                if (active.boolValue)
                {
                    EditorGUILayout.PropertyField(
                        applyImmediately,
                        new GUIContent(
                            "Apply to Current Occupants",
                            "Apply the group's effects immediately instead of waiting for the area's next application interval."));
                }
                return;
            }

            if (value is RunEventEffectRouteResponse)
            {
                SerializedProperty routeId =
                    response.FindPropertyRelative("routeId");
                DrawEventEffectRouteSelector(routeId);
                EditorGUILayout.LabelField(
                    "Runs the selected Manual Reaction recipe using this " +
                    "delivery contact's point and source caster.",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }

            if (value is DestroyDeliveryResponse)
            {
                EditorGUILayout.LabelField(
                    "Removes this persistent delivery after earlier actions run.",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }

            DrawManagedReferenceChildren(response);
        }

        private void DrawReactiveEffectGroupSelector(
            SerializedProperty groupId)
        {
            var labels = new List<string> { "<Select Group>" };
            var ids = new List<string> { string.Empty };
            int selected = 0;
            bool storedSelectionExists =
                string.IsNullOrWhiteSpace(groupId.stringValue);
            for (int i = 0; i < reactiveEffectGroups.arraySize; i++)
            {
                SerializedProperty group = reactiveEffectGroups
                    .GetArrayElementAtIndex(i);
                string name = group.FindPropertyRelative("displayName")
                    .stringValue;
                string id = group.FindPropertyRelative("stableId")
                    .stringValue;
                labels.Add(string.IsNullOrWhiteSpace(name)
                    ? $"Reactive Effect Group {i + 1}"
                    : name);
                ids.Add(id);
                if (string.Equals(
                        groupId.stringValue,
                        id,
                        System.StringComparison.Ordinal))
                {
                    selected = i + 1;
                    storedSelectionExists = true;
                }
            }

            if (!storedSelectionExists)
                labels[0] = "<Missing Group>";

            EditorGUI.BeginChangeCheck();
            int chosen = EditorGUILayout.Popup(
                new GUIContent(
                    "Effect Group",
                    "Choose which named Reactive Effect Group this action changes."),
                selected,
                labels.ToArray());
            if (EditorGUI.EndChangeCheck())
                groupId.stringValue = ids[chosen];

            if (reactiveEffectGroups.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "Create a Reactive Effect Group in Composition first.",
                    MessageType.Warning);
            }
        }

        private void DrawEventEffectRouteSelector(
            SerializedProperty routeId)
        {
            var labels = new List<string> { "<Select Recipe>" };
            var ids = new List<string> { string.Empty };
            int selected = 0;
            bool storedSelectionExists =
                string.IsNullOrWhiteSpace(routeId.stringValue);
            for (int i = 0; i < eventEffectRoutes.arraySize; i++)
            {
                SerializedProperty route = eventEffectRoutes
                    .GetArrayElementAtIndex(i);
                if ((SpellEventType)route.FindPropertyRelative("trigger")
                        .enumValueIndex != SpellEventType.ManualReaction)
                {
                    continue;
                }

                string name = route.FindPropertyRelative("displayName")
                    .stringValue;
                string id = route.FindPropertyRelative("stableId")
                    .stringValue;
                labels.Add(string.IsNullOrWhiteSpace(name)
                    ? $"Event Effect Recipe {i + 1}"
                    : name);
                ids.Add(id);
                if (string.Equals(
                        routeId.stringValue,
                        id,
                        System.StringComparison.Ordinal))
                {
                    selected = ids.Count - 1;
                    storedSelectionExists = true;
                }
            }

            if (!storedSelectionExists)
                labels[0] = "<Missing Recipe>";

            EditorGUI.BeginChangeCheck();
            int chosen = EditorGUILayout.Popup(
                new GUIContent(
                    "Effect Recipe",
                    "Choose a recipe whose WHEN event is Started by a Delivery Reaction."),
                selected,
                labels.ToArray());
            if (EditorGUI.EndChangeCheck())
                routeId.stringValue = ids[chosen];

            if (ids.Count == 1)
            {
                EditorGUILayout.HelpBox(
                    "Create an Event Effect Recipe whose WHEN event is " +
                    "'Started by a Delivery Reaction' first.",
                    MessageType.Warning);
            }
        }

        private void DrawReactionWarnings(
            SerializedProperty policy,
            SerializedProperty cooldown,
            SerializedProperty responses)
        {
            if ((InteractionTriggerPolicy)policy.enumValueIndex ==
                    InteractionTriggerPolicy.EveryContact &&
                cooldown.floatValue <= 0f)
            {
                EditorGUILayout.HelpBox(
                    "Every matching contact has no minimum delay. Stay " +
                    "contacts may execute this reaction repeatedly.",
                    MessageType.Warning);
            }

            bool hasPulse = false;
            bool hasImmediatePulse = false;
            int pulseIndex = -1;
            int destroyIndex = -1;
            for (int i = 0; i < responses.arraySize; i++)
            {
                SerializedProperty response =
                    responses.GetArrayElementAtIndex(i);
                object value = response.managedReferenceValue;
                if (value is PulseEffectsResponse)
                {
                    hasPulse = true;
                    if (pulseIndex < 0)
                        pulseIndex = i;
                }
                else if (value is DestroyDeliveryResponse &&
                         destroyIndex < 0)
                {
                    destroyIndex = i;
                }
                else if (value is ActivateDeliveryResponse)
                {
                    SerializedProperty active =
                        response.FindPropertyRelative("active");
                    SerializedProperty immediate =
                        response.FindPropertyRelative(
                            "pulseEffectsImmediately");
                    hasImmediatePulse |= active.boolValue &&
                                         immediate.boolValue;
                }

                if (value is SetReactiveEffectGroupActiveResponse)
                {
                    string groupId = response
                        .FindPropertyRelative("groupId").stringValue;
                    if (string.IsNullOrWhiteSpace(groupId) ||
                        !ReactiveEffectGroupExists(groupId))
                    {
                        EditorGUILayout.HelpBox(
                            "A Reactive Effect Group action does not reference " +
                            "an existing group.",
                            MessageType.Warning);
                    }
                }

                if (value is RunEventEffectRouteResponse)
                {
                    string routeId = response
                        .FindPropertyRelative("routeId").stringValue;
                    if (string.IsNullOrWhiteSpace(routeId) ||
                        !EventEffectRouteExists(routeId))
                    {
                        EditorGUILayout.HelpBox(
                            "A Run Event Effect Recipe action does not " +
                            "reference an existing Manual Reaction recipe.",
                            MessageType.Warning);
                    }
                }
            }

            SpellDefinition spell = target as SpellDefinition;
            if (hasPulse && spell != null && spell.EffectSlots.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "An action applies effects to occupants, but this spell " +
                    "has no equipped effects.",
                    MessageType.Warning);
            }

            if (destroyIndex >= 0 &&
                destroyIndex < responses.arraySize - 1)
            {
                EditorGUILayout.HelpBox(
                    "Destroy This Delivery is not the final action. Move it " +
                    "to the bottom so later actions run before removal.",
                    MessageType.Warning);
            }

            if (hasImmediatePulse && hasPulse && pulseIndex >= 0)
            {
                EditorGUILayout.HelpBox(
                    "Activation is set to apply effects immediately and the " +
                    "reaction also has an Apply Effects action. Occupants may " +
                    "receive the effects twice.",
                    MessageType.Warning);
            }
        }

        private string BuildReactionSummary(
            SerializedProperty filter,
            SerializedProperty policy,
            SerializedProperty cooldown,
            SerializedProperty responses)
        {
            string trigger = BuildTriggerSummary(filter);
            string frequency;
            switch ((InteractionTriggerPolicy)policy.enumValueIndex)
            {
                case InteractionTriggerPolicy.EveryContact:
                    frequency = "on every matching contact";
                    break;
                case InteractionTriggerPolicy.OncePerSourceDelivery:
                    frequency = "once per individual source delivery";
                    break;
                default:
                    frequency = "only once";
                    break;
            }

            if ((InteractionTriggerPolicy)policy.enumValueIndex !=
                    InteractionTriggerPolicy.OnceTotal &&
                cooldown.floatValue > 0f)
            {
                frequency +=
                    $", with at least {cooldown.floatValue:0.##} seconds between triggers";
            }

            var actions = new List<string>();
            for (int i = 0; i < responses.arraySize; i++)
            {
                actions.Add(BuildResponseSummary(
                    responses.GetArrayElementAtIndex(i)));
            }

            string actionText = actions.Count > 0
                ? string.Join(", then ", actions)
                : "perform no actions";
            return $"When {trigger} touches this delivery, trigger {frequency}, " +
                   $"then {actionText}.";
        }

        private static string BuildTriggerSummary(SerializedProperty filter)
        {
            SerializedProperty conditions =
                filter.FindPropertyRelative("conditions");
            if (conditions.arraySize == 0)
                return "any V2 delivery";

            var summaries = new List<string>();
            for (int i = 0; i < conditions.arraySize; i++)
            {
                summaries.Add(BuildConditionSummary(
                    conditions.GetArrayElementAtIndex(i),
                    compact: false));
            }

            InteractionFilterMatchMode mode =
                (InteractionFilterMatchMode)filter
                    .FindPropertyRelative("matchMode").enumValueIndex;
            string joiner = mode == InteractionFilterMatchMode.All
                ? " and "
                : " or ";
            return string.Join(joiner, summaries);
        }

        private static string BuildConditionSummary(
            SerializedProperty condition,
            bool compact)
        {
            object value = condition?.managedReferenceValue;
            bool inverted = condition != null &&
                            condition.FindPropertyRelative("inverted")
                                .boolValue;
            string comparison = inverted ? "is not" : "is";
            string carries = inverted ? "does not carry" : "carries";

            string summary;
            if (value is InteractionRelationshipCondition)
            {
                summary = $"source relationship {comparison} " +
                    EnumText(condition.FindPropertyRelative("relationship"));
            }
            else if (value is InteractionCasterTeamCondition)
            {
                summary = $"caster team {comparison} " +
                    EnumText(condition.FindPropertyRelative("team"));
            }
            else if (value is InteractionSpellCondition)
            {
                summary = $"source spell {comparison} " +
                    ObjectText(condition.FindPropertyRelative("spell"));
            }
            else if (value is InteractionSpellCategoryCondition)
            {
                string category = condition.FindPropertyRelative("category")
                    .stringValue;
                summary = $"spell category {comparison} " +
                    (string.IsNullOrWhiteSpace(category)
                        ? "Unassigned"
                        : category);
            }
            else if (value is InteractionContactPhaseCondition)
            {
                var phases = (DeliveryContactPhase)condition
                    .FindPropertyRelative("acceptedPhases").intValue;
                summary = inverted
                    ? $"contact phase excludes {Nicify(phases.ToString())}"
                    : $"contact phase includes {Nicify(phases.ToString())}";
            }
            else if (value is InteractionDeliveryCondition)
            {
                summary = $"delivery module {comparison} " +
                    ObjectText(condition.FindPropertyRelative("delivery"));
            }
            else if (value is InteractionEffectCondition)
            {
                summary = $"source {carries} effect " +
                    ObjectText(condition.FindPropertyRelative("effect"));
            }
            else if (value is InteractionDamageTypeCondition)
            {
                summary = $"source {carries} damage type " +
                    ObjectText(condition.FindPropertyRelative("damageType"));
            }
            else
            {
                summary = value is DeliveryInteractionCondition known
                    ? known.DisplayName
                    : "an unassigned trigger rule";
            }

            if (!compact || string.IsNullOrEmpty(summary))
                return summary;
            return char.ToUpperInvariant(summary[0]) + summary.Substring(1);
        }

        private string BuildResponseSummary(
            SerializedProperty response)
        {
            object value = response?.managedReferenceValue;
            if (value is ActivateDeliveryResponse)
            {
                bool active = response.FindPropertyRelative("active").boolValue;
                bool pulse = active && response
                    .FindPropertyRelative("pulseEffectsImmediately").boolValue;
                if (!active)
                    return "deactivate this delivery";
                return pulse
                    ? "activate this delivery and immediately apply its effects"
                    : "activate this delivery";
            }
            if (value is PulseEffectsResponse)
                return "apply this spell's effects to current occupants";
            if (value is SetReactiveEffectGroupActiveResponse)
            {
                string groupId = response.FindPropertyRelative("groupId")
                    .stringValue;
                string groupName = GetReactiveEffectGroupName(groupId);
                bool active = response.FindPropertyRelative("active")
                    .boolValue;
                bool immediate = active && response
                    .FindPropertyRelative(
                        "applyToCurrentOccupantsImmediately").boolValue;
                if (!active)
                    return $"disable reactive effect group {groupName}";
                return immediate
                    ? $"enable reactive effect group {groupName} and apply it to current occupants"
                    : $"enable reactive effect group {groupName}";
            }
            if (value is RunEventEffectRouteResponse)
            {
                string routeId = response.FindPropertyRelative("routeId")
                    .stringValue;
                return $"run event effect recipe " +
                       $"{GetEventEffectRouteName(routeId)}";
            }
            if (value is DestroyDeliveryResponse)
                return "destroy this delivery";
            return "run an unassigned action";
        }

        private bool ReactiveEffectGroupExists(string groupId)
        {
            return !string.IsNullOrWhiteSpace(groupId) &&
                   GetReactiveEffectGroupName(groupId) != "Unassigned";
        }

        private bool EventEffectRouteExists(string routeId)
        {
            return !string.IsNullOrWhiteSpace(routeId) &&
                   GetEventEffectRouteName(routeId) != "Unassigned";
        }

        private string GetEventEffectRouteName(string routeId)
        {
            if (string.IsNullOrWhiteSpace(routeId))
                return "Unassigned";

            for (int i = 0; i < eventEffectRoutes.arraySize; i++)
            {
                SerializedProperty route = eventEffectRoutes
                    .GetArrayElementAtIndex(i);
                if (!string.Equals(
                        route.FindPropertyRelative("stableId").stringValue,
                        routeId,
                        System.StringComparison.Ordinal))
                {
                    continue;
                }

                string name = route.FindPropertyRelative("displayName")
                    .stringValue;
                return string.IsNullOrWhiteSpace(name)
                    ? $"Event Effect Recipe {i + 1}"
                    : name;
            }

            return "Unassigned";
        }

        private string GetReactiveEffectGroupName(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
                return "Unassigned";

            for (int i = 0; i < reactiveEffectGroups.arraySize; i++)
            {
                SerializedProperty group = reactiveEffectGroups
                    .GetArrayElementAtIndex(i);
                if (!string.Equals(
                        group.FindPropertyRelative("stableId").stringValue,
                        groupId,
                        System.StringComparison.Ordinal))
                {
                    continue;
                }

                string name = group.FindPropertyRelative("displayName")
                    .stringValue;
                return string.IsNullOrWhiteSpace(name)
                    ? $"Reactive Effect Group {i + 1}"
                    : name;
            }

            return "Unassigned";
        }

        private static string EnumText(SerializedProperty property)
        {
            if (property == null)
                return "Unassigned";
            int index = property.enumValueIndex;
            if (index >= 0 && index < property.enumDisplayNames.Length)
                return property.enumDisplayNames[index];
            return Nicify(property.intValue.ToString());
        }

        private static string ObjectText(SerializedProperty property)
        {
            return property != null && property.objectReferenceValue != null
                ? property.objectReferenceValue.name
                : "Unassigned";
        }

        private static string Nicify(string value)
        {
            return ObjectNames.NicifyVariableName(
                string.IsNullOrWhiteSpace(value)
                    ? "Unassigned"
                    : value.Replace(", ", " or "));
        }

        private void DrawEffectSlot(int index)
        {
            DrawEffectSlot(effectSlots, index);
        }

        private static void DrawEffectSlot(
            SerializedProperty slots,
            int index)
        {
            SerializedProperty slot = slots.GetArrayElementAtIndex(index);
            SerializedProperty effect = slot.FindPropertyRelative("effect");
            SerializedProperty settings = slot.FindPropertyRelative("settings");
            var definition = effect.objectReferenceValue as EffectDefinition;
            string title = definition != null
                ? definition.DisplayName
                : $"Effect {index + 1}";

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    slot.isExpanded = EditorGUILayout.Foldout(
                        slot.isExpanded,
                        new GUIContent(
                            title,
                            definition != null
                                ? GetEffectTooltip(definition)
                                : "Expand or collapse this effect and its per-spell settings."),
                        toggleOnLabelClick: true);

                    GUI.enabled = index > 0;
                    if (GUILayout.Button(
                            new GUIContent(
                                "▲",
                                "Move this effect earlier in the application order."),
                            GUILayout.Width(24f)))
                    {
                        slots.MoveArrayElement(index, index - 1);
                        GUI.enabled = true;
                        return;
                    }

                    GUI.enabled = index < slots.arraySize - 1;
                    if (GUILayout.Button(
                            new GUIContent(
                                "▼",
                                "Move this effect later in the application order."),
                            GUILayout.Width(24f)))
                    {
                        slots.MoveArrayElement(index, index + 1);
                        GUI.enabled = true;
                        return;
                    }

                    GUI.enabled = true;
                    if (GUILayout.Button(
                            new GUIContent(
                                "×",
                                "Remove this effect slot."),
                            GUILayout.Width(24f)))
                    {
                        slots.DeleteArrayElementAtIndex(index);
                        return;
                    }
                }

                if (!slot.isExpanded)
                    return;

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(
                    effect,
                    new GUIContent(
                        "Effect",
                        definition != null
                            ? GetEffectTooltip(definition)
                            : "Choose the reusable effect module. The settings below are stored independently for this spell."));
                if (EditorGUI.EndChangeCheck())
                {
                    definition = effect.objectReferenceValue as EffectDefinition;
                    settings.managedReferenceValue = definition != null
                        ? definition.CreateDefaultSettings()
                        : null;
                }

                if (definition == null)
                {
                    EditorGUILayout.HelpBox(
                        "Assign an effect module to populate its settings.",
                        MessageType.Info);
                    return;
                }

                EnsureCompatibleSettings(definition, settings);
                if (definition.SettingsType == null)
                {
                    EditorGUILayout.HelpBox(
                        "This effect currently uses its shared asset settings.",
                        MessageType.None);
                    return;
                }

                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField(
                    new GUIContent(
                        "Per-Spell Settings",
                        "These values affect only this effect slot on this spell. They do not change the reusable effect asset."),
                    EditorStyles.boldLabel);
                DrawManagedReferenceChildren(settings);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(
                            new GUIContent(
                                "Reset to Defaults",
                                "Replace this slot's values with the reusable effect asset's current defaults."),
                            GUILayout.Width(140f)))
                        settings.managedReferenceValue =
                            definition.CreateDefaultSettings();
                }
            }
        }

        private static void EnsureCompatibleSettings(
            EffectDefinition definition,
            SerializedProperty settings)
        {
            System.Type expected = definition.SettingsType;
            object current = settings.managedReferenceValue;
            if (expected == null)
            {
                if (current != null)
                    settings.managedReferenceValue = null;
                return;
            }

            if (current == null || current.GetType() != expected)
                settings.managedReferenceValue = definition.CreateDefaultSettings();
        }

        private static void EnsureCompatibleSettings(
            DeliveryDefinition definition,
            SerializedProperty settings)
        {
            System.Type expected = definition.SettingsType;
            object current = settings.managedReferenceValue;
            if (expected == null)
            {
                if (current != null)
                    settings.managedReferenceValue = null;
                return;
            }

            if (current == null || current.GetType() != expected)
            {
                settings.managedReferenceValue =
                    definition.CreateDefaultSettings();
            }
        }

        private static void DrawManagedReferenceChildren(
            SerializedProperty property)
        {
            if (property.managedReferenceValue is
                CasterMovementEffectSettings)
            {
                DrawCasterMovementSettings(property);
                return;
            }

            if (property.managedReferenceValue is
                ProjectileDeliverySettings)
            {
                DrawProjectileDeliverySettings(property);
                return;
            }

            if (property.managedReferenceValue is
                SpatialForceEffectSettings)
            {
                DrawSpatialForceSettings(property);
                return;
            }

            if (property.managedReferenceValue is
                ActorRelocationEffectSettings)
            {
                DrawActorRelocationSettings(property);
                return;
            }

            SerializedProperty iterator = property.Copy();
            SerializedProperty end = iterator.GetEndProperty();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren) &&
                   !SerializedProperty.EqualContents(iterator, end))
            {
                EditorGUILayout.PropertyField(iterator, includeChildren: true);
                enterChildren = false;
            }
        }

        private static void DrawSpatialForceSettings(
            SerializedProperty property)
        {
            EditorGUILayout.PropertyField(
                property.FindPropertyRelative("direction"));
            EditorGUILayout.PropertyField(
                property.FindPropertyRelative("center"));
            EditorGUILayout.PropertyField(
                property.FindPropertyRelative("strength"));
            EditorGUILayout.PropertyField(
                property.FindPropertyRelative("maximumSpeed"));
            SerializedProperty falloff =
                property.FindPropertyRelative("falloff");
            EditorGUILayout.PropertyField(falloff);
            if ((SpatialForceFalloff)falloff.enumValueIndex !=
                SpatialForceFalloff.None)
            {
                EditorGUILayout.PropertyField(
                    property.FindPropertyRelative("falloffDistance"));
            }
            EditorGUILayout.PropertyField(
                property.FindPropertyRelative("stopDistance"));
            EditorGUILayout.PropertyField(
                property.FindPropertyRelative("duration"));
            SerializedProperty respect =
                property.FindPropertyRelative("respectObstacles");
            EditorGUILayout.PropertyField(respect);
            if (respect.boolValue)
            {
                EditorGUILayout.PropertyField(
                    property.FindPropertyRelative("obstacleMask"));
            }
        }

        private static void DrawActorRelocationSettings(
            SerializedProperty property)
        {
            SerializedProperty mode = property.FindPropertyRelative("mode");
            EditorGUILayout.PropertyField(mode);
            EditorGUILayout.PropertyField(
                property.FindPropertyRelative("destination"));
            if ((ActorRelocationMode)mode.enumValueIndex ==
                ActorRelocationMode.Travel)
            {
                EditorGUILayout.PropertyField(
                    property.FindPropertyRelative("speed"));
            }
            EditorGUILayout.PropertyField(
                property.FindPropertyRelative("maximumDistance"));
            EditorGUILayout.PropertyField(
                property.FindPropertyRelative("destinationOffset"));
            SerializedProperty lineOfSight =
                property.FindPropertyRelative("requireLineOfSight");
            EditorGUILayout.PropertyField(lineOfSight);
            if (lineOfSight.boolValue)
            {
                EditorGUILayout.PropertyField(
                    property.FindPropertyRelative("obstacleMask"));
                EditorGUILayout.PropertyField(
                    property.FindPropertyRelative("clampToObstacles"));
                EditorGUILayout.PropertyField(
                    property.FindPropertyRelative("obstacleSkin"));
            }
            if ((ActorRelocationMode)mode.enumValueIndex ==
                ActorRelocationMode.InstantTeleport)
            {
                EditorGUILayout.PropertyField(
                    property.FindPropertyRelative("preserveVelocity"));
            }
        }

        private static void DrawProjectileDeliverySettings(
            SerializedProperty property)
        {
            EditorGUILayout.PropertyField(
                property.FindPropertyRelative("playerTargeting"));

            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField("Shot Pattern", EditorStyles.boldLabel);
            SerializedProperty emission =
                property.FindPropertyRelative("emission");
            SerializedProperty emissionPattern =
                emission.FindPropertyRelative("pattern");
            SerializedProperty projectileCount =
                emission.FindPropertyRelative("projectileCount");
            EditorGUILayout.PropertyField(emissionPattern);
            EditorGUILayout.PropertyField(projectileCount);
            ProjectileEmissionPattern pattern =
                (ProjectileEmissionPattern)emissionPattern.enumValueIndex;
            if (pattern == ProjectileEmissionPattern.Fan ||
                pattern == ProjectileEmissionPattern.RandomCone)
            {
                EditorGUILayout.PropertyField(
                    emission.FindPropertyRelative("spreadAngle"));
            }
            if (projectileCount.intValue > 1)
            {
                EditorGUILayout.PropertyField(
                    emission.FindPropertyRelative("shotInterval"));
                if (emission.FindPropertyRelative("shotInterval")
                        .floatValue > 0f)
                {
                    EditorGUILayout.PropertyField(
                        emission.FindPropertyRelative(
                            "reAimSequentialShots"));
                }
            }

            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField("Hit Shape", EditorStyles.boldLabel);
            SerializedProperty shape =
                property.FindPropertyRelative("shotShape");
            SerializedProperty hitShape =
                shape.FindPropertyRelative("hitShape");
            EditorGUILayout.PropertyField(hitShape);
            ProjectileHitShape shapeValue =
                (ProjectileHitShape)hitShape.enumValueIndex;
            if (shapeValue == ProjectileHitShape.InstantBeam)
            {
                EditorGUILayout.PropertyField(
                    shape.FindPropertyRelative("beamWidth"));
            }
            else if (shapeValue == ProjectileHitShape.Cone)
            {
                EditorGUILayout.PropertyField(
                    shape.FindPropertyRelative("coneAngle"));
            }

            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField("Range and Collision", EditorStyles.boldLabel);
            if (shapeValue == ProjectileHitShape.Projectile)
            {
                EditorGUILayout.PropertyField(
                    property.FindPropertyRelative("projectilePrefab"));
                EditorGUILayout.PropertyField(
                    property.FindPropertyRelative(
                        "allowPrefabGameplayComponents"));
                EditorGUILayout.PropertyField(
                    property.FindPropertyRelative("speed"));
            }
            EditorGUILayout.PropertyField(property.FindPropertyRelative("range"));
            EditorGUILayout.PropertyField(
                property.FindPropertyRelative("collisionRadius"));
            EditorGUILayout.PropertyField(
                property.FindPropertyRelative("collisionMask"));
            EditorGUILayout.PropertyField(
                property.FindPropertyRelative("pierceTargets"));
            if (property.FindPropertyRelative("pierceTargets").boolValue ||
                shapeValue == ProjectileHitShape.Cone)
            {
                EditorGUILayout.PropertyField(
                    property.FindPropertyRelative("maximumTargetHits"));
            }
            EditorGUILayout.PropertyField(
                property.FindPropertyRelative("stopOnBlockedCollider"));
            if (shapeValue == ProjectileHitShape.Projectile)
            {
                EditorGUILayout.PropertyField(
                    property.FindPropertyRelative("castBufferSize"));
            }

            if (shapeValue == ProjectileHitShape.Projectile)
            {
                EditorGUILayout.Space(3f);
                EditorGUILayout.LabelField("Projectile Travel", EditorStyles.boldLabel);
                SerializedProperty motion =
                    property.FindPropertyRelative("motion");
                SerializedProperty motionPattern =
                    motion.FindPropertyRelative("pattern");
                EditorGUILayout.PropertyField(motionPattern);
                ProjectileMotionPattern motionValue =
                    (ProjectileMotionPattern)motionPattern.enumValueIndex;
                if (motionValue == ProjectileMotionPattern.Homing)
                {
                    EditorGUILayout.PropertyField(
                        motion.FindPropertyRelative("homingAcquireRadius"));
                    EditorGUILayout.PropertyField(
                        motion.FindPropertyRelative("homingTurnRate"));
                }
                else if (motionValue == ProjectileMotionPattern.Boomerang)
                {
                    EditorGUILayout.PropertyField(
                        motion.FindPropertyRelative("returnAtRangeFraction"));
                    EditorGUILayout.PropertyField(
                        motion.FindPropertyRelative("returnCatchRadius"));
                }
            }

            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField("Distance Falloff", EditorStyles.boldLabel);
            SerializedProperty falloff =
                property.FindPropertyRelative("falloff");
            SerializedProperty falloffMode =
                falloff.FindPropertyRelative("mode");
            EditorGUILayout.PropertyField(falloffMode);
            if ((ProjectileDamageFalloff)falloffMode.enumValueIndex !=
                ProjectileDamageFalloff.None)
            {
                EditorGUILayout.PropertyField(
                    falloff.FindPropertyRelative("minimumPotency"));
                EditorGUILayout.PropertyField(
                    falloff.FindPropertyRelative("curveExponent"));
            }
        }

        private static void DrawCasterMovementSettings(
            SerializedProperty property)
        {
            SerializedProperty destinationSource =
                property.FindPropertyRelative("destinationSource");
            SerializedProperty instantaneous =
                property.FindPropertyRelative("instantaneous");
            SerializedProperty requireLineOfSight =
                property.FindPropertyRelative("requireLineOfSight");

            EditorGUILayout.PropertyField(
                destinationSource,
                new GUIContent(
                    "Destination",
                    "Aimed Point creates a dash/blink. Delivery Event Point uses the point supplied by an Event Effect Recipe, such as a projectile impact."));
            EditorGUILayout.PropertyField(
                property.FindPropertyRelative("maximumDistance"));
            EditorGUILayout.PropertyField(instantaneous);
            if (!instantaneous.boolValue)
            {
                EditorGUILayout.PropertyField(
                    property.FindPropertyRelative("speed"));
            }

            EditorGUILayout.PropertyField(requireLineOfSight);
            if (requireLineOfSight.boolValue)
            {
                EditorGUILayout.PropertyField(
                    property.FindPropertyRelative("obstructionMask"));
            }

            if ((CasterMovementDestinationSource)
                    destinationSource.enumValueIndex ==
                CasterMovementDestinationSource.DeliveryEventPoint)
            {
                SerializedProperty keepOutside =
                    property.FindPropertyRelative(
                        "keepOutsideHitSurface");
                EditorGUILayout.PropertyField(
                    keepOutside,
                    new GUIContent(
                        "Keep Outside Hit Surface",
                        "Offset the destination by the caster's collider size so an impact teleport does not place the caster inside a wall or target."));
                if (keepOutside.boolValue)
                {
                    EditorGUILayout.PropertyField(
                        property.FindPropertyRelative(
                            "extraSurfaceClearance"),
                        new GUIContent(
                            "Extra Surface Clearance",
                            "Additional world-space gap left between the caster and the impacted surface."));
                }
            }
        }

        private void ShowAddEffectMenu()
        {
            ShowEffectDefinitionMenu(AddEffectSlot);
        }

        private static void ShowEffectDefinitionMenu(
            System.Action<EffectDefinition> addEffect)
        {
            var menu = new GenericMenu();
            menu.AddItem(
                new GUIContent(
                    "Empty Slot",
                    "Add an unassigned effect slot to fill later."),
                false,
                () => addEffect(null));
            menu.AddSeparator(string.Empty);

            // Unity's type index can omit ScriptableObject subclasses declared
            // in shared source files even though LoadAssetAtPath can load them
            // correctly. Combine the fast type search with a direct scan of
            // the canonical Effects content folder so every usable effect is
            // presented to designers.
            var candidatePaths = new HashSet<string>();
            string[] guids = AssetDatabase.FindAssets("t:EffectDefinition");
            for (int i = 0; i < guids.Length; i++)
                candidatePaths.Add(AssetDatabase.GUIDToAssetPath(guids[i]));

            const string effectsContentFolder =
                "Assets/Combat/SkillSystemV2/Content/Effects";
            if (AssetDatabase.IsValidFolder(effectsContentFolder))
            {
                string[] contentGuids = AssetDatabase.FindAssets(
                    string.Empty,
                    new[] { effectsContentFolder });
                for (int i = 0; i < contentGuids.Length; i++)
                {
                    candidatePaths.Add(
                        AssetDatabase.GUIDToAssetPath(contentGuids[i]));
                }
            }

            var definitions = new List<EffectDefinition>();
            foreach (string path in candidatePaths)
            {
                EffectDefinition definition =
                    AssetDatabase.LoadAssetAtPath<EffectDefinition>(path);
                if (definition != null)
                    definitions.Add(definition);
            }

            definitions.Sort((a, b) => string.Compare(
                a.DisplayName,
                b.DisplayName,
                System.StringComparison.OrdinalIgnoreCase));

            for (int i = 0; i < definitions.Count; i++)
            {
                EffectDefinition definition = definitions[i];
                string path = AssetDatabase.GetAssetPath(definition);
                string folder = Path.GetFileName(
                    Path.GetDirectoryName(path));
                string label = string.IsNullOrWhiteSpace(folder)
                    ? definition.DisplayName
                    : $"{definition.DisplayName} ({folder})";
                menu.AddItem(
                    new GUIContent(
                        label,
                        GetEffectTooltip(definition)),
                    false,
                    () => addEffect(definition));
            }

            menu.ShowAsContext();
        }

        private static string GetEffectTooltip(EffectDefinition definition)
        {
            if (definition == null)
                return "An unassigned effect slot.";

            switch (definition.GetType().Name)
            {
                case "DamageEffectDefinition":
                    return "Remove health from the recipient once, with an optional Damage Type.";
                case "DamageOverTimeEffectDefinition":
                    return "Place a timed effect on the recipient that deals damage repeatedly.";
                case "HealingEffectDefinition":
                    return "Restore health to the recipient and optionally revive defeated characters.";
                case "ImpulseEffectDefinition":
                    return "Push or pull the recipient in a chosen direction.";
                case "ResourceEffectDefinition":
                    return "Add, remove, or set a gameplay resource such as Action Points.";
                case "SpawnEffectDefinition":
                    return "Create a prefab at a hit point, target, cast origin, or chosen point.";
                case "TriggerSpellEffectDefinition":
                    return "Queue another Spell Definition as the next stage of a cast chain.";
                case "GameplaySignalEffectDefinition":
                    return "Send a general event that another game system can listen for.";
                case "ApplyStatusEffectDefinition":
                    return "Add a reusable timed or stacking Status Definition to the recipient.";
                case "RemoveStatusEffectDefinition":
                    return "Remove some or all stacks of one exact Status Definition.";
                case "CasterMovementEffectDefinition":
                    return "Move the caster toward an aimed point or a delivery event point, creating dashes, blinks, and impact teleports.";
                case "LegacyMovementSlowEffectDefinition":
                    return "Change movement speed for a duration or while the target remains inside an area. Multipliers below 1 slow; values above 1 speed up.";
                case "LegacyProjectileReflectEffectDefinition":
                    return "Reverse supported enemy projectiles, make them player-owned, and allow them to damage enemies.";
                default:
                    return $"Apply the reusable {definition.DisplayName} effect. Its settings are stored independently for this spell.";
            }
        }

        private static string GetDeliveryTooltip(
            DeliveryDefinition definition)
        {
            if (definition == null)
                return "No delivery is currently assigned.";

            switch (definition.GetType().Name)
            {
                case "ProjectileDeliveryDefinition":
                    return "Launch an object that travels through the world and reports targets, blockers, and where it stops.";
                case "MeleeArcDeliveryDefinition":
                    return "Check a cone or circle around the caster and apply effects to valid objects inside the swing.";
                case "LingeringAreaDeliveryDefinition":
                    return "Create a persistent area that tracks occupants, repeats effects, and can react to other deliveries.";
                case "AreaDeliveryDefinition":
                    return "Check one circular area at the chosen point and apply effects once.";
                case "PointClickDeliveryDefinition":
                    return "Choose a world point, then apply effects to the caster while preserving that destination for movement effects.";
                case "TripWireDeliveryDefinition":
                    return "Choose two clear points and connect them with a persistent line that activates when a valid object crosses it.";
                case "ProximityMineDeliveryDefinition":
                    return "Place a mine that arms, watches for nearby valid objects, and applies its effects when triggered.";
                case "GrenadeDeliveryDefinition":
                    return "Throw a timed object toward a point. It can stop normally, stick to a surface, or bounce before detonating.";
                case "RicochetProjectileDeliveryDefinition":
                    return "Launch a projectile that reflects from surfaces and can optionally be knocked away by basic attacks.";
                case "InstantTargetDeliveryDefinition":
                    return "Select one object and apply effects to it immediately.";
                case "SelfDeliveryDefinition":
                    return "Apply effects directly to the caster without requiring aim.";
                default:
                    return $"Use the reusable {definition.DisplayName} delivery module.";
            }
        }

        private void AddEffectSlot(EffectDefinition definition)
        {
            serializedObject.Update();
            int index = effectSlots.arraySize;
            effectSlots.arraySize++;
            SerializedProperty slot = effectSlots.GetArrayElementAtIndex(index);
            slot.FindPropertyRelative("effect").objectReferenceValue = definition;
            slot.FindPropertyRelative("settings").managedReferenceValue =
                definition != null ? definition.CreateDefaultSettings() : null;
            slot.isExpanded = true;
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawChainSafety()
        {
            EditorGUILayout.LabelField(
                "Chain Safety",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(maximumChainDepth);
            EditorGUILayout.PropertyField(maximumRootActivations);
            EditorGUILayout.Space();
        }

        private void DrawValidation()
        {
            if (targets.Length != 1)
                return;

            var spell = (SpellDefinition)target;
            issues.Clear();
            spell.CollectValidationIssues(issues);

            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Spell definition is valid.",
                    MessageType.Info);
                return;
            }

            for (int i = 0; i < issues.Count; i++)
            {
                EditorGUILayout.HelpBox(
                    issues[i].Message,
                    ToMessageType(issues[i].Severity));
            }
        }

        private static MessageType ToMessageType(
            SpellValidationSeverity severity)
        {
            switch (severity)
            {
                case SpellValidationSeverity.Error:
                    return MessageType.Error;
                case SpellValidationSeverity.Warning:
                    return MessageType.Warning;
                default:
                    return MessageType.Info;
            }
        }
    }
}
