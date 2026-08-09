using System;

namespace Core;

/// <summary>
/// Orders stops into a short closed tour: nearest-neighbour for a starting order, then 2-opt
/// until no improving swap remains.
///
/// <para><b>Why not the genetic solver in Utilities.</b> That one derives its distances
/// internally from <c>Vector2</c> positions and draws from a static <c>Random</c>. Route
/// generation needs the opposite of both - the cost between two stops is the length of the
/// walkable path between them, not the straight line, and a pinned seed has to reproduce the
/// same tour. Reusing it would mean replacing its distance function, its randomness and its
/// point type, which is most of it. At the handful of stops a grind route carries, 2-opt is
/// also effectively optimal and needs no tuning.</para>
/// </summary>
public static class TourSolver
{
    /// <summary>
    /// Guards the 2-opt loop against pathological input. Each pass is O(n²) and passes stop
    /// as soon as one makes no improvement, so this is only ever reached if costs are
    /// inconsistent (an asymmetric matrix can cycle).
    /// </summary>
    private const int MaxPasses = 64;

    /// <summary>
    /// Visit order over <paramref name="cost"/>, a square matrix where <c>cost[a,b]</c> is
    /// the travel cost from stop a to stop b. The tour is closed: the leg from the last
    /// index back to the first is part of what is minimised.
    /// </summary>
    public static int[] Solve(float[,] cost) => Solve(cost, out _, out _);

    /// <summary>
    /// As <see cref="Solve(float[,])"/>, also reporting the tour cost before and after the
    /// 2-opt pass so callers can see whether the optimisation earned its place.
    /// </summary>
    public static int[] Solve(float[,] cost, out float greedyCost, out float optimisedCost)
    {
        int n = cost.GetLength(0);
        if (n <= 3)
        {
            int[] trivial = new int[n];
            for (int i = 0; i < n; i++)
            {
                trivial[i] = i;
            }

            greedyCost = optimisedCost = TourCost(trivial, cost);
            return trivial;
        }

        int[] tour = NearestNeighbour(cost, n);
        greedyCost = TourCost(tour, cost);

        Improve(tour, cost, n);
        optimisedCost = TourCost(tour, cost);

        return tour;
    }

    /// <summary>
    /// Always starts at stop 0 so the result is reproducible; 2-opt removes most of the
    /// difference a start choice would make anyway.
    /// </summary>
    private static int[] NearestNeighbour(float[,] cost, int n)
    {
        int[] tour = new int[n];
        bool[] used = new bool[n];

        tour[0] = 0;
        used[0] = true;

        for (int i = 1; i < n; i++)
        {
            int from = tour[i - 1];
            int best = -1;
            float bestCost = float.MaxValue;

            for (int candidate = 0; candidate < n; candidate++)
            {
                if (used[candidate] || cost[from, candidate] >= bestCost)
                {
                    continue;
                }

                bestCost = cost[from, candidate];
                best = candidate;
            }

            // Every cost equal or unreachable - take the first unused, in index order, so
            // the outcome stays deterministic.
            if (best < 0)
            {
                for (int candidate = 0; candidate < n; candidate++)
                {
                    if (!used[candidate])
                    {
                        best = candidate;
                        break;
                    }
                }
            }

            tour[i] = best;
            used[best] = true;
        }

        return tour;
    }

    /// <summary>
    /// Repeatedly reverses the segment between two stops when doing so shortens the tour.
    /// This is what removes the self-crossings a nearest-neighbour order leaves behind -
    /// a crossing is always longer than the uncrossed alternative.
    /// </summary>
    private static void Improve(int[] tour, float[,] cost, int n)
    {
        for (int pass = 0; pass < MaxPasses; pass++)
        {
            bool improved = false;

            for (int i = 0; i < n - 1; i++)
            {
                for (int k = i + 2; k < n; k++)
                {
                    // Skip the pair that would reverse the whole tour - same cycle.
                    if (i == 0 && k == n - 1)
                    {
                        continue;
                    }

                    int a = tour[i];
                    int b = tour[i + 1];
                    int c = tour[k];
                    int d = tour[(k + 1) % n];

                    float before = cost[a, b] + cost[c, d];
                    float after = cost[a, c] + cost[b, d];

                    if (after >= before - float.Epsilon)
                    {
                        continue;
                    }

                    Array.Reverse(tour, i + 1, k - i);
                    improved = true;
                }
            }

            if (!improved)
            {
                return;
            }
        }
    }

    /// <summary>Total closed-tour cost, for logging and comparison.</summary>
    public static float TourCost(int[] tour, float[,] cost)
    {
        float total = 0f;

        for (int i = 0; i < tour.Length; i++)
        {
            total += cost[tour[i], tour[(i + 1) % tour.Length]];
        }

        return total;
    }
}
