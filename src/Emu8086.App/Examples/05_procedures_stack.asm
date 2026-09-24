; Procedures and the stack: recursive factorial.
; Watch SP and the Stack panel while stepping with F11.
include 'emu8086.inc'
#make_COM#
org 100h

    mov cx, 7
    call factorial          ; AX = 7! = 5040
    PRINT '7! = '
    call PRINT_NUM_UNS
    ret

; input: CX = n, output: AX = n!
factorial proc
    cmp cx, 1
    ja recurse
    mov ax, 1               ; 0! = 1! = 1
    ret
recurse:
    push cx                 ; save n on the stack
    dec cx
    call factorial          ; AX = (n-1)!
    pop cx                  ; restore n
    mul cx                  ; AX = n * (n-1)!
    ret
factorial endp

DEFINE_PRINT_NUM_UNS
END
