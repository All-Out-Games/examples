13
107374182401
1734461834030333 1737998854061429500
{
  "name": "Fish Buyer",
  "local_enabled": true,
  "local_position": {
    "X": -6.5064291954040527,
    "Y": 6.5360507965087891
  },
  "local_rotation": 0,
  "local_scale": {
    "X": 1,
    "Y": 1
  },
  "previous_sibling": "1821215960650447:1735855244509224500",
  "next_sibling": "197690942118438:1738446251876438800",
  "parent": "134953159748543:1738126268880005500",
  "spawn_as_networked_entity": true
},
{
  "cid": 1,
  "aoid": "1734461834231070:1737998854061471800",
  "component_type": "Internal_Component",
  "internal_component_type": "Spine_Animator",
  "data": {
    "skeleton_data_asset": "$AO/schleem/playercharacter.spine",
    "ordered_skins": [
      "base/crewchsia",
      "full_character/pirate_crewmate_full"
    ]
  }
},
{
  "cid": 3,
  "aoid": "1734461834413524:1737998854061510600",
  "component_type": "Internal_Component",
  "internal_component_type": "Interactable",
  "data": {
    "text": "Sell Fish"
  }
},
{
  "cid": 2,
  "aoid": "1734461834462451:1737998854061521000",
  "component_type": "Mono_Component",
  "mono_component_type": "NpcTrigger",
  "data": {
    "Interactable": "1734461834413524:1737998854061510600",
    "SpineAnimator": "1734461834231070:1737998854061471800",
    "overrideSkin": true,
    "colorIndex": 3
  }
}
