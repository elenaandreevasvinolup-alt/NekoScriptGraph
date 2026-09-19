#include <vector>
#include <map>
#include <string>

// Только тело метода становится блоками. Дженерики — в текстовом слоте типа,
// поэтому это полноценный блок, а не сырой фрагмент.
int crunch(int n)
{
    std::vector<int> sieve = std::vector<int>();
    std::map<std::string, int> tally = std::map<std::string, int>();
    int primes = 0;
    int i = 0;
    while (i <= n)
    {
        sieve.push_back(0);
        i++;
    }
    i = 2;
    while (i <= n)
    {
        if (sieve[i] == 0)
        {
            primes += 1;
            tally["p"] = i;
            int j = i * i;
            while (j <= n)
            {
                sieve[j] = 1;
                j += i;
            }
        }
        i++;
    }
    int mask = (1 << 8) - 1;
    int folded = (primes & mask) ^ (primes >> 3);
    int bonus = folded > 0 ? folded : -1;
    return primes + bonus + (int)tally.size();
}
