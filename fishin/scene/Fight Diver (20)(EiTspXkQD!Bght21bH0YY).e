13
377957122049
1276787096962051 1738872611449816600
{
  "name": "Fight Diver (20)",
  "local_enabled": true,
  "local_position": {
    "X": 36.8549995422363281,
    "Y": 5.6789999008178711
  },
  "local_rotation": 0,
  "local_scale": {
    "X": -1,
    "Y": 1
  },
  "previous_sibling": "175056893539904:1738441436005790000",
  "next_sibling": "1277009504769564:1738872658770529400",
  "parent": "134953159748543:1738126268880005500",
  "spawn_as_networked_entity": true
},
{
  "cid": 1,
  "aoid": "1276787097168522:1738872611449859700",
  "component_type": "Internal_Component",
  "internal_component_type": "Spine_Animator",
  "data": {
    "skeleton_data_asset": "Animations/player/playercharacter.spine",
    "ordered_skins": [
      "face/scuba_mask",
      "body/scuba_suit",
      "base/pink1"
    ]
  }
},
{
  "cid": 3,
  "aoid": "1276787097281369:1738872611449883700",
  "component_type": "Internal_Component",
  "internal_component_type": "Interactable",
  "data": {
    "text": "Fight Diver (Lv. 20)"
  }
},
{
  "cid": 2,
  "aoid": "1276787097326771:1738872611449893500",
  "component_type": "Mono_Component",
  "mono_component_type": "FightNPC",
  "data": {
    "Interactable": "1276787097281369:1738872611449883700",
    "SpineAnimator": "1276787097168522:1738872611449859700",
    "battleType": 1,
    "medianTeamLevel": 20
  }
}
