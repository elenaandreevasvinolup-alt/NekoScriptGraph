# Только тело функции становится блоками; шапка переносится как есть.
# Внутри — письменное подмножество NSG.
def crunch(n):
    primes = 0
    i = 2
    while i <= n:
        is_prime = 1
        j = 2
        while j * j <= i:
            if i % j == 0:
                is_prime = 0
            j += 1
        primes += is_prime
        i += 1
    mask = (1 << 8) - 1
    folded = (primes & mask) ^ (primes >> 3)
    bonus = -1
    if folded > 0:
        bonus = folded
    return primes + bonus
