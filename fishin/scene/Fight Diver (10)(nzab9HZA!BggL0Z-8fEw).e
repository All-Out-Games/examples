13
339302416386
175056893539904 1738441436005790000
{
  "name": "Fight Diver (10)",
  "local_enabled": true,
  "local_position": {
    "X": 5.8552942276000977,
    "Y": 8.6789054870605469
  },
  "local_rotation": 0,
  "local_scale": {
    "X": -1,
    "Y": 1
  },
  "previous_sibling": "790788144804297:1738769207764413300",
  "next_sibling": "1276787096962051:1738872611449816600",
  "parent": "134953159748543:1738126268880005500",
  "spawn_as_networked_entity": true
},
{
  "cid": 1,
  "aoid": "175056893789239:1738441436005841800",
  "component_type": "Internal_Component",
  "internal_component_type": "Spine_Animator",
  "data": {
    "skeleton_data_asset": "Animations/player/playercharacter.spine",
    "ordered_skins": [
      "base/green1",
      "face/scuba_mask",
      "body/scuba_suit"
    ]
  }
},
{
  "cid": 3,
  "aoid": "175056893974184:1738441436005881200",
  "component_type": "Internal_Component",
  "internal_component_type": "Interactable",
  "data": {
    "text": "Fight Diver (Lv. 10)"
  }
},
{
  "cid": 2,
  "aoid": "175114642917777:1738441448293187200",
  "component_type": "Mono_Component",
  "mono_component_type": "FightNPC",
  "data": {
    "Interactable": "175056893974184:1738441436005881200",
    "SpineAnimator": "175056893789239:1738441436005841800",
    "battleType": 1,
    "medianTeamLevel": 10
  }
}
