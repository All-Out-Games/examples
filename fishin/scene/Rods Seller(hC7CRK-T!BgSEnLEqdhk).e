13
51539607553
145336362643411 1734469090626099300
{
  "name": "Rods Seller",
  "local_enabled": true,
  "local_position": {
    "X": 6.9029998779296875,
    "Y": 3.5236716270446777
  },
  "local_rotation": 0,
  "local_scale": {
    "X": -1,
    "Y": 1
  },
  "next_sibling": "1821215960650447:1735855244509224500",
  "parent": "134953159748543:1738126268880005500",
  "spawn_as_networked_entity": true
},
{
  "cid": 1,
  "aoid": "145365665570975:1734469096860894000",
  "component_type": "Internal_Component",
  "internal_component_type": "Spine_Animator",
  "data": {
    "skeleton_data_asset": "$AO/schleem/playercharacter.spine",
    "ordered_skins": [
      "full_character/fisherman_full",
      "base/crewchsia"
    ]
  }
},
{
  "cid": 3,
  "aoid": "154271043705230:1734470991661567600",
  "component_type": "Internal_Component",
  "internal_component_type": "Interactable",
  "data": {
    "text": "Buy Rods"
  }
},
{
  "cid": 2,
  "aoid": "1852453348623789:1735861890742400600",
  "component_type": "Mono_Component",
  "mono_component_type": "NpcTrigger",
  "data": {
    "Interactable": "154271043705230:1734470991661567600",
    "SpineAnimator": "145365665570975:1734469096860894000",
    "shopType": 1,
    "overrideSkin": true
  }
}
