; Printing a number in binary, hexadecimal and decimal.
include 'emu8086.inc'
#make_COM#
org 100h

    mov bx, 2026

    PRINT 'bin: '
    mov cx, 16
bits:
    shl bx, 1               ; top bit goes into CF
    mov dl, '0'
    adc dl, 0               ; '0' + CF
    mov ah, 02h
    int 21h
    loop bits               ; after 16 shifts BX is 0 again...
    mov bx, 2026            ; ...so reload it

    PRINTN ''
    PRINT 'hex: '
    mov cx, 4
nibbles:
    rol bx, 4               ; bring the top nibble to the bottom
    mov dl, bl
    and dl, 0Fh
    add dl, '0'
    cmp dl, '9'
    jbe show
    add dl, 7               ; 'A'..'F'
show:
    mov ah, 02h
    int 21h
    loop nibbles

    PRINTN ''
    PRINT 'dec: '
    mov ax, bx
    call PRINT_NUM_UNS
    ret

DEFINE_PRINT_NUM_UNS
END
