﻿using System.Collections.Generic;
using System.Linq;

namespace SyncStreamAPI.Models.GameModels.Chess;
#nullable enable
public class ChessLogic
{
    private static List<ChessGame> ChessGames { get; } = new();

    public static ChessGame? GetChessGame(string uniqueId)
    {
        return ChessGames.FirstOrDefault(x => x.UniqueId == uniqueId);
    }

    public static ChessGame AddChessGame(string uniqueId, Member lightPlayer, Member darkPlayer,
        bool lightPlayerAi = false, bool darkPlayerAi = false)
    {
        var idx = ChessGames.FindIndex(x => x.UniqueId == uniqueId);
        var game = new ChessGame(lightPlayer, darkPlayer, uniqueId, lightPlayerAi, darkPlayerAi);
        if (idx == -1)
            ChessGames.Add(game);
        else
            ChessGames[idx] = game;

        return game;
    }

    public static void RemoveChessGame(string uniqueId)
    {
        var idx = ChessGames.FindIndex(x => x.UniqueId == uniqueId);
        if (idx == -1) return;

        ChessGames.RemoveAt(idx);
    }
}