namespace EscapeRoom
{
    /// <summary>Player evidence for Puzzles 2 and 3 (same wording as the on-screen demo evidence cards).</summary>
    public static class PuzzleClues
    {
        public const string BlackoutATitle = "INCIDENT LOG A-17";
        public const string BlackoutABody =
            "Colours were repainted during maintenance.\n\n" +
            "Relay diagnostics (top row, then bottom):\n" +
            "1: 1011     2: 0110     3: 1101     4: 0100\n" +
            "5: 1110     6: 1001     7: 0011     8: 1010\n\n" +
            "You control the relays at the lab console. Describe these signatures to B; ask for the maintenance rule.";

        public const string BlackoutBTitle = "MAINTENANCE REVISION 04";
        public const string BlackoutBBody =
            "A stable relay contains EXACTLY TWO 1s. Turn all other relays OFF. Ignore colours.\n\n" +
            "Read each row left to right: ON=1, OFF=0. Bit weights are 8, 4, 2, 1.\n\n" +
            "Digit 1 = top row in decimal.\nDigit 2 = bottom row in decimal.\nDigit 3 = top XOR bottom in decimal.\n" +
            "XOR: equal bits = 0; different bits = 1.\n\n" +
            "Enter all three digits at the server-room console; both operators then authorise together for two seconds.";

        public const string GhostATitle = "NETWORK TELEMETRY";
        public const string GhostABody =
            "All links are ONE-WAY; costs are milliseconds.\n\n" +
            "S > A: 4       S > B: 3\nA > C: 3       A > D: 1\nB > D: 2       B > E: 2\n" +
            "C > V: 5       D > V: 1       E > V: 4\n\n" +
            "Start at S and guide a packet through connected nodes to V at the lab console. " +
            "Discuss link timings with B. B holds the updated security policy.";

        public const string GhostBTitle = "SECURITY DIRECTIVE";
        public const string GhostBBody =
            "Router D is compromised: NEVER use D.\n" +
            "An authorised route starts at S, ends at V, follows directed links, and costs AT MOST 10 ms.\n\n" +
            "Ask A for the route's total latency T.\nSHIFT = T mod 5 (the remainder after dividing by 5).\n\n" +
            "Decode STIR by shifting every letter BACKWARD by SHIFT in the alphabet; wrap A back to Z.\n" +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ\n\n" +
            "Set the decoder shift, validate the route, enter four letters, then both authorise for two seconds.";
    }
}
