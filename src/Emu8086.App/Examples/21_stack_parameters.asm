; Passing parameters on the stack and reading them through BP.
include 'emu8086.inc'
#make_COM#
org 100h

    push 300            ; second parameter
    push 45             ; first parameter
    call add_two        ; AX = 345
    add sp, 4           ; caller removes the parameters
    call PRINT_NUM_UNS
    ret

; stack after "mov bp, sp":  [bp+0] old BP, [bp+2] return address, [bp+4] first, [bp+6] second
add_two proc
    push bp
    mov bp, sp
    mov ax, [bp+4]
    add ax, [bp+6]
    pop bp
    ret
add_two endp

DEFINE_PRINT_NUM_UNS
END
