; Loops: print the digits 0..9 with LOOP, then count down with a conditional jump.
#make_COM#
org 100h

    mov cx, 10              ; LOOP decrements CX and repeats while CX != 0
    mov dl, '0'
next_digit:
    mov ah, 02h             ; DOS: print the character in DL
    int 21h
    inc dl
    loop next_digit

    mov dl, 13
    int 21h
    mov dl, 10
    int 21h

    mov bl, 9               ; count down 9..0 using CMP + JGE
countdown:
    mov dl, bl
    add dl, '0'
    int 21h
    dec bl
    cmp bl, 0
    jge countdown
    ret
