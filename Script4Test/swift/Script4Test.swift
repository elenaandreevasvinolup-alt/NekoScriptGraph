// Только тело функции становится блоками; шапка переносится как есть.
// Внутри — письменное подмножество NSG.
func crunch(_ n: Int) -> Int {
    var primes: Int = 0
    var i: Int = 2
    while i <= n {
        var isPrime: Int = 1
        var j: Int = 2
        while j * j <= i {
            if i % j == 0 {
                isPrime = 0
            }
            j += 1
        }
        primes += isPrime
        i += 1
    }
    let mask: Int = (1 << 8) - 1
    let folded: Int = (primes & mask) ^ (primes >> 3)
    let bonus: Int = folded > 0 ? folded : -1
    return primes + bonus
}
