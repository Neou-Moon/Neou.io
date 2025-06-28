using UnityEngine;
using System.Collections.Generic;

public class UsernameGenerator : MonoBehaviour
{
    private List<string> adjectives = new List<string>
    {
        "Happy", "Brave", "Clever", "Swift", "Mighty", "Fierce", "Gentle", "Wise", "Loyal", "Calm",
        "Eager", "Jolly", "Kind", "Proud", "Silly", "Witty", "Zany", "Bold", "Daring", "Nimble",
        "Vivid", "Radiant", "Jubilant", "Valiant", "Adept", "Fuzzy", "Gleaming", "Heroic", "Iron", "Jovial",
        "Covert", "Steel", "Amber", "Crimson", "Emerald", "Azure", "Violet", "Bronze", "Copper", "Golden",
        "Silent", "Rusty", "Frosty", "Solar", "Lunar", "Stellar", "Cosmic", "Atomic", "Neon", "Electric",
        "Misty", "Frozen", "Fiery", "Breezy", "Stormy", "Dusty", "Gritty", "Sandy", "Metallic", "Glowing",
        "Hollow", "Twin", "Noble", "Royal", "Ancient", "Timeless", "Epic", "Mythic", "Legendary", "Astral",
        "Quantum", "Hyper", "Mega", "Turbo", "Ultra", "Neon", "Cyber", "Pixel", "Retro", "Synth",
        "Chaos", "Void", "Rogue", "Stealth", "Phantom", "Shadow", "Ninja", "Samurai", "Viking", "Pirate",
        "Wizard", "Dragon", "Phoenix", "Griffin", "Yeti", "Kraken", "Alien", "Robot", "Android", "Cyborg"
    };

    private List<string> nouns = new List<string>
    {
        "Tiger", "Falcon", "Eagle", "Wolf", "Bear", "Shark", "Dragon", "Phoenix", "Lion", "Panther",
        "Rhino", "Mantis", "Scorpion", "Viper", "Cobra", "Raptor", "Hawk", "Raven", "Owl", "Fox",
        "Lynx", "Puma", "Jaguar", "Grizzly", "Orca", "Leviathan", "Griffin", "Basilisk", "Chimera", "Kitsune",
        "Storm", "River", "Mountain", "Ocean", "Comet", "Blaze", "Thunder", "Shadow", "Vortex", "Titan",
        "Moon", "Sun", "Star", "Galaxy", "Nebula", "Quasar", "Aurora", "Tsunami", "Earth", "Mars",
        "Jupiter", "Saturn", "Orbit", "Eclipse", "Tundra", "Jungle", "Desert", "Canyon", "Volcano", "Glacier",
        "Drone", "Mech", "Bot", "Golem", "Sentinel", "Overlord", "Warden", "Guardian", "Paladin", "Templar",
        "Nomad", "Vagabond", "Mercenary", "Ronin", "Corsair", "Buccaneer", "Raider", "Marauder", "Outlaw", "Renegade",
        "Blade", "Axe", "Hammer", "Arrow", "Shield", "Helm", "Gauntlet", "Armor", "Spear", "Scythe",
        "Revolver", "Rifle", "Katana", "Crossbow", "Grenade", "Missile", "Rocket", "Satellite", "Hologram", "Nanobot"
    };

    public string GenerateUsername()
    {
        if (adjectives.Count == 0 || nouns.Count == 0)
        {
            Debug.LogError("UsernameGenerator: Adjectives or nouns list is empty.");
            return "DefaultUser";
        }

        string adjective = adjectives[Random.Range(0, adjectives.Count)];
        string noun = nouns[Random.Range(0, nouns.Count)];
        int number = Random.Range(0, 100);
        string username = $"{adjective}{noun}{number}";
        Debug.Log($"UsernameGenerator: Generated username: {username}");
        return username;
    }
}