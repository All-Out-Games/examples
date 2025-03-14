13
55834574849
1821215960650447 1735855244509224500
{
  "name": "Boat Seller",
  "local_enabled": true,
  "local_position": {
    "X": 14.8474721908569336,
    "Y": 1.7539165019989014
  },
  "local_rotation": 0,
  "local_scale": {
    "X": -1,
    "Y": 1
  },
  "previous_sibling": "145336362643411:1734469090626099300",
  "next_sibling": "1734461834030333:1737998854061429500",
  "parent": "134953159748543:1738126268880005500",
  "spawn_as_networked_entity": true
},
{
  "cid": 1,
  "aoid": "1821215960844275:1735855244509265100",
  "component_type": "Internal_Component",
  "internal_component_type": "Spine_Animator",
  "data": {
    "skeleton_data_asset": "Animations/player/playercharacter.spine",
    "ordered_skins": [
      "full_character/pirate_full",
      "base/purple1"
    ]
  }
},
{
  "cid": 3,
  "aoid": "1821215960994487:1735855244509297100",
  "component_type": "Internal_Component",
  "internal_component_type": "Interactable",
  "data": {
    "text": "Buy Boats"
  }
},
{
  "cid": 2,
  "aoid": "1852531585848450:1735861907388598200",
  "component_type": "Mono_Component",
  "mono_component_type": "NpcTrigger",
  "data": {
    "Interactable": "1821215960994487:1735855244509297100",
    "SpineAnimator": "1821215960844275:1735855244509265100",
    "shopType": 2
  }
}
